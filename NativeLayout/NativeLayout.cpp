// GPL-3.0-or-later. Modified edition by fhzzzs.
// Uses Windows SDK XAML Diagnostics. No Windhawk, machine-code patching, or shell file changes.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <ocidl.h>
#include <xamlom.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <utility>
#undef GetCurrentTime
#include <cstddef>
#include <cwchar>
#include <string>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Media.h>

using namespace winrt;
using namespace Windows::UI::Xaml;
namespace {
constexpr CLSID SlotClsid{0x9d6ab7c2,0x502e,0x4ae0,{0xa2,0x1d,0x3f,0x77,0x1e,0x9b,0x84,0x33}};
struct alignas(8) Lease {
    volatile LONG width, owner;
    volatile LONG64 tick;
    volatile LONG appliedWidth, state; // 0 idle, 1 applied, -1 incompatible/conflict
    volatile LONG64 reply;
};
static_assert(sizeof(Lease)==32 && offsetof(Lease,tick)==8 && offsetof(Lease,reply)==24);
std::wstring MappingName(){return L"Local\\LGSTray.Rings9.NativeLayout."+std::to_wstring(GetCurrentProcessId());}
HWND PrimaryBar(){HWND h=FindWindowW(L"Shell_TrayWnd",nullptr);DWORD pid=0;if(h)GetWindowThreadProcessId(h,&pid);return pid==GetCurrentProcessId()?h:nullptr;}
// Access these UI objects exclusively from the primary taskbar UI thread.
FrameworkElement start{nullptr};
InstanceHandle startHandle{};
UINT_PTR timerId{};
Windows::Foundation::IInspectable oldMarginLocal{nullptr},oldTransformLocal{nullptr};
Thickness initialMargin{}, ourMargin{};
Media::TranslateTransform translation{nullptr};
bool ownsMargin=false, ownsTransform=false, conflict=false;
int currentWidth=0;
std::atomic<bool> sessionActive{false};
bool Same(Thickness a,Thickness b){return std::abs(a.Left-b.Left)<.01&&std::abs(a.Right-b.Right)<.01&&std::abs(a.Top-b.Top)<.01&&std::abs(a.Bottom-b.Bottom)<.01;}
void Restore(){
    try{
        if(start && ownsTransform && start.RenderTransform()==translation){
            if(oldTransformLocal==DependencyProperty::UnsetValue())start.ClearValue(UIElement::RenderTransformProperty());
            else start.SetValue(UIElement::RenderTransformProperty(),oldTransformLocal);
        }
    }catch(...){}
    try{
        if(start && ownsMargin && Same(start.Margin(),ourMargin)){
            if(oldMarginLocal==DependencyProperty::UnsetValue())start.ClearValue(FrameworkElement::MarginProperty());
            else start.SetValue(FrameworkElement::MarginProperty(),oldMarginLocal);
        }
    }catch(...){}
    ownsMargin=ownsTransform=false;currentWidth=0;translation=nullptr;oldMarginLocal=nullptr;oldTransformLocal=nullptr;
}
bool ValidStart(FrameworkElement const& el){
    if(!el||get_class_name(el)!=L"Taskbar.ExperienceToggleButton"||Automation::AutomationProperties::GetAutomationId(el)!=L"StartButton")return false;
    auto parent=Media::VisualTreeHelper::GetParent(el).try_as<FrameworkElement>();
    return parent && parent.Name()==L"TaskbarFrameRepeater" && el.XamlRoot() && el.Visibility()==Visibility::Visible && el.ActualWidth()>10 && el.FlowDirection()==FlowDirection::LeftToRight;
}
int Apply(int width, double barWidth){
    if(width<=0){Restore();conflict=false;return 0;}
    if(conflict)return -1;
    if(!ValidStart(start)){Restore();return 0;}
    auto root=start.XamlRoot();
    if(root.Size().Width < barWidth*.55 || root.Size().Width > barWidth+20){Restore();return -1;}
    if((ownsMargin&&!Same(start.Margin(),ourMargin))||(ownsTransform&&start.RenderTransform()!=translation)){
        Restore();conflict=true;return -1;
    }
    if(!ownsMargin){
        oldMarginLocal=start.ReadLocalValue(FrameworkElement::MarginProperty());
        if(oldMarginLocal!=DependencyProperty::UnsetValue()){
            try{(void)unbox_value<Thickness>(oldMarginLocal);}catch(...){conflict=true;return -1;}
        }
        initialMargin=start.Margin();
        if(!std::isfinite(initialMargin.Right)||initialMargin.Right<0){conflict=true;return -1;}
    }
    // This is an actual measure/arrange change: the native repeater reserves width after Start.
    if(!ownsMargin||width!=currentWidth){
        ourMargin=initialMargin;ourMargin.Right+=width;start.Margin(ourMargin);ownsMargin=true;currentWidth=width;
        return 0; // Wait one UI tick for Explorer's layout to settle.
    }
    // In centered mode Start's normal slot is centered too. Move only Start's visual
    // position to the root's leading edge; the positive margin keeps the native app group clear.
    auto rootElement=root.Content().try_as<UIElement>();
    if(!rootElement){Restore();return -1;}
    auto p=start.TransformToVisual(rootElement).TransformPoint({0,0});
    double naturalX=p.X-(ownsTransform?translation.X():0.0);
    if(!std::isfinite(naturalX)||naturalX< -8 || naturalX>barWidth){Restore();return -1;}
    const double wanted=std::min(0.0,4.0-naturalX);
    if(!ownsTransform && wanted < -.5){
        oldTransformLocal=start.ReadLocalValue(UIElement::RenderTransformProperty());
        // Never replace an unknown transform or a binding used by another taskbar modifier.
        auto existing=start.RenderTransform();
        if(existing){auto a=existing.TransformPoint({0,0});auto b=existing.TransformPoint({1,1});
            if(std::abs(a.X)>.01||std::abs(a.Y)>.01||std::abs(b.X-1)>.01||std::abs(b.Y-1)>.01){Restore();conflict=true;return -1;}}
        if(oldTransformLocal!=DependencyProperty::UnsetValue() && oldTransformLocal && !oldTransformLocal.try_as<Media::Transform>()){
            Restore();conflict=true;return -1;
        }
        translation=Media::TranslateTransform();translation.X(wanted);start.RenderTransform(translation);ownsTransform=true;return 0;
    }
    if(ownsTransform&&std::abs(translation.X()-wanted)>.5){translation.X(wanted);return 0;}
    // Managed UI Automation still independently checks the actual screen-coordinate slot.
    return 1;
}
void CALLBACK Tick(HWND,UINT,UINT_PTR,DWORD){
    if(!sessionActive.load()){Restore();conflict=false;return;}
    HANDLE mapping=nullptr;
    try{mapping=OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,MappingName().c_str());}catch(...){Restore();return;}
    auto* lease=mapping?static_cast<Lease*>(MapViewOfFile(mapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(Lease))):nullptr;
    if(!lease){if(mapping)CloseHandle(mapping);Restore();conflict=false;return;}
    int w=0,state=0;
    try{
        auto now=GetTickCount64();auto heartbeat=static_cast<ULONGLONG>(InterlockedCompareExchange64(&lease->tick,0,0));
        int req=InterlockedCompareExchange(&lease->width,0,0);int owner=InterlockedCompareExchange(&lease->owner,0,0);
        HWND bar=PrimaryBar();RECT bounds{};UINT dpi=bar?GetDpiForWindow(bar):0;
        if(owner>0&&heartbeat<=now&&now-heartbeat<2500&&req>0&&req<=1024&&bar&&dpi&&GetSystemMetrics(SM_CMONITORS)==1&&GetWindowRect(bar,&bounds)&&bounds.right-bounds.left>bounds.bottom-bounds.top&&req*dpi/96.0<(bounds.right-bounds.left)*.45){
            w=req;state=Apply(w,(bounds.right-bounds.left)*96.0/dpi);
        }else{Restore();conflict=false;}
    }catch(...){Restore();state=-1;}
    InterlockedExchange(&lease->appliedWidth,state==1?w:0);
    InterlockedExchange(&lease->state,state);
    InterlockedExchange64(&lease->reply,static_cast<LONG64>(GetTickCount64()));
    UnmapViewOfFile(lease);CloseHandle(mapping);
}
// The diagnostics session enumerates the existing visual tree and notifies future changes.
struct Watcher : implements<Watcher,IVisualTreeServiceCallback,non_agile>{
    com_ptr<IXamlDiagnostics> diagnostics;
    explicit Watcher(com_ptr<IXamlDiagnostics> d):diagnostics(std::move(d)){}
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation,VisualElement el,VisualMutationType mutation) noexcept override{
        try{
            HWND bar=PrimaryBar();if(!bar||GetWindowThreadProcessId(bar,nullptr)!=GetCurrentThreadId())return S_OK;
            if(mutation==Remove){if(el.Handle==startHandle){Restore();start=nullptr;startHandle=0;conflict=false;}return S_OK;}
            if(mutation!=Add||!el.Type||wcscmp(el.Type,L"Taskbar.ExperienceToggleButton")!=0)return S_OK;
            Windows::Foundation::IInspectable obj{nullptr};
            if(FAILED(diagnostics->GetIInspectableFromHandle(el.Handle,reinterpret_cast<::IInspectable**>(put_abi(obj)))))return S_OK;
            auto f=obj.try_as<FrameworkElement>();
            if(!f||Automation::AutomationProperties::GetAutomationId(f)!=L"StartButton")return S_OK;
            if(start!=f){Restore();start=f;startHandle=el.Handle;conflict=false;}
            // Pin only this specific loaded module so the UI timer cannot run into an unloaded DLL.
            if(!timerId){HMODULE module=nullptr;
                if(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&Tick),&module))timerId=SetTimer(nullptr,0,250,Tick);
            }
        }catch(...){}
        return S_OK;
    }
};
struct Site : implements<Site,IObjectWithSite,non_agile>{
    com_ptr<IVisualTreeService> service;
    com_ptr<Watcher> watcher;
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* value) noexcept override{
        try{
            sessionActive.store(false);
            if(service&&watcher){service->UnadviseVisualTreeChange(watcher.get());watcher=nullptr;service=nullptr;}
            if(!value)return S_OK;
            com_ptr<IUnknown> unknown;unknown.copy_from(value);
            service=unknown.as<IVisualTreeService>();watcher=make_self<Watcher>(unknown.as<IXamlDiagnostics>());
            sessionActive.store(true);
            HRESULT hr=service->AdviseVisualTreeChange(watcher.get());
            if(FAILED(hr))sessionActive.store(false);
            return hr;
        }catch(...){sessionActive.store(false);return to_hresult();}
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID riid,void** ppv) noexcept override{
        if(!ppv)return E_POINTER;*ppv=nullptr;return service?service->QueryInterface(riid,ppv):E_FAIL;
    }
};
struct Factory : implements<Factory,IClassFactory,non_agile>{
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID iid,void** out) noexcept override{
        if(!out)return E_POINTER;*out=nullptr;if(outer)return CLASS_E_NOAGGREGATION;
        try{return make<Site>().as(iid,out);}catch(...){return to_hresult();}
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override{return S_OK;}
};
}
// Only the Explorer shell's primary taskbar process may instantiate the provider.
extern "C" __declspec(dllexport) HRESULT WINAPI DllGetClassObject(REFCLSID clsid,REFIID iid,void** out){
    if(!out)return E_POINTER;*out=nullptr;if(clsid!=SlotClsid||!PrimaryBar())return CLASS_E_CLASSNOTAVAILABLE;
    try{return make<Factory>().as(iid,out);}catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT WINAPI DllCanUnloadNow(){return S_FALSE;}
