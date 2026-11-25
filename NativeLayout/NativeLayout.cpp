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
#include <vector>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Automation.Peers.h>
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
std::wstring MappingName(){return L"Local\\LGSTray.Rings9.NativeLayout.4."+std::to_wstring(GetCurrentProcessId());}
HWND PrimaryBar(){HWND h=FindWindowW(L"Shell_TrayWnd",nullptr);DWORD pid=0;if(h)GetWindowThreadProcessId(h,&pid);return pid==GetCurrentProcessId()?h:nullptr;}
// Access these UI objects exclusively from the primary taskbar UI thread.
FrameworkElement start{nullptr}, slotElement{nullptr};
InstanceHandle startHandle{};
std::vector<std::pair<InstanceHandle,FrameworkElement>> candidates;
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
        if(slotElement && ownsMargin && Same(slotElement.Margin(),ourMargin)){
            if(oldMarginLocal==DependencyProperty::UnsetValue())slotElement.ClearValue(FrameworkElement::MarginProperty());
            else slotElement.SetValue(FrameworkElement::MarginProperty(),oldMarginLocal);
        }
    }catch(...){}
    ownsMargin=ownsTransform=false;currentWidth=0;translation=nullptr;oldMarginLocal=nullptr;oldTransformLocal=nullptr;slotElement=nullptr;
}
bool IsStart(FrameworkElement const& el){
    if(!el)return false;
    auto type=get_class_name(el);
    if(type!=L"Taskbar.ExperienceToggleButton"&&type!=L"Windows.UI.Xaml.Controls.Primitives.ToggleButton")return false;
    return Automation::AutomationProperties::GetAutomationId(el)==L"StartButton"||el.Name()==L"StartButton";
}
// Reserve space on the actual repeater item, not on a nested visual whose
// margin might never participate in taskbar measure/arrange.
FrameworkElement FindRepeaterItem(FrameworkElement const& el){
    if(!el)return nullptr;
    auto item=el;
    for(int depth=0;depth<5;depth++){
        auto parent=Media::VisualTreeHelper::GetParent(item).try_as<FrameworkElement>();
        if(!parent)return nullptr;
        if(parent.Name()==L"TaskbarFrameRepeater")return item;
        item=parent;
    }
    return nullptr;
}
bool ValidStart(FrameworkElement const& el){
    return IsStart(el)&&el.XamlRoot()&&el.Visibility()==Visibility::Visible&&el.ActualWidth()>10&&el.FlowDirection()==FlowDirection::LeftToRight;
}
// The same Explorer UI thread can own multiple taskbars. Identify the primary
// Start button by its actual automation screen bounds, never by thread alone.
bool TryPrimaryBounds(FrameworkElement const& el,RECT const& bounds,Windows::Foundation::Rect& result){
    if(!ValidStart(el))return false;
    try{
        auto peer=Automation::Peers::FrameworkElementAutomationPeer::FromElement(el);
        if(!peer)peer=Automation::Peers::FrameworkElementAutomationPeer::CreatePeerForElement(el);
        if(!peer)return false;
        auto r=peer.GetBoundingRectangle();
        double x=r.X+r.Width/2.0,y=r.Y+r.Height/2.0;
        if(!(r.Width>0&&r.Height>0&&x>=bounds.left&&x<bounds.right&&y>=bounds.top&&y<bounds.bottom))return false;
        result=r;return true;
    }catch(...){return false;}
}
bool OnPrimaryBar(FrameworkElement const& el,RECT const& bounds){Windows::Foundation::Rect r{};return TryPrimaryBounds(el,bounds,r);}
bool SelectPrimary(RECT const& bounds){
    if(start&&OnPrimaryBar(start,bounds))return true;
    Restore();start=nullptr;startHandle=0;conflict=false;
    for(auto const& candidate:candidates){
        if(!OnPrimaryBar(candidate.second,bounds))continue;
        if(start){start=nullptr;startHandle=0;return false;} // ambiguous: do not modify either
        start=candidate.second;startHandle=candidate.first;
    }
    return bool(start);
}
int Apply(int width, RECT const& bounds, UINT dpi){
    const double barWidth=(bounds.right-bounds.left)*96.0/dpi;
    if(width<=0){Restore();conflict=false;return 0;}
    if(conflict)return -1;
    if(!ValidStart(start)){Restore();return 2;}
    auto item=FindRepeaterItem(start);
    if(!item){Restore();return 5;}
    if(slotElement&&slotElement!=item){Restore();return 3;}
    slotElement=item;
    auto root=start.XamlRoot();
    if(root.Size().Width < 48 || root.Size().Width > barWidth+20){Restore();return -1;}
    if((ownsMargin&&!Same(slotElement.Margin(),ourMargin))||(ownsTransform&&start.RenderTransform()!=translation)){
        Restore();conflict=true;return -1;
    }
    if(!ownsMargin){
        oldMarginLocal=slotElement.ReadLocalValue(FrameworkElement::MarginProperty());
        if(oldMarginLocal!=DependencyProperty::UnsetValue()){
            try{(void)unbox_value<Thickness>(oldMarginLocal);}catch(...){conflict=true;return -1;}
        }
        initialMargin=slotElement.Margin();
        if(!std::isfinite(initialMargin.Right)||initialMargin.Right<0){conflict=true;return -1;}
    }
    // This is an actual measure/arrange change: the native repeater reserves width after Start.
    if(!ownsMargin||width!=currentWidth){
        ourMargin=initialMargin;ourMargin.Right+=width;slotElement.Margin(ourMargin);ownsMargin=true;currentWidth=width;
        return 3; // Wait one UI tick for Explorer's layout to settle.
    }
    // In centered mode Start's normal slot is centered too. Move only Start's visual
    // position to the root's leading edge; the positive margin keeps the native app group clear.
    // XAML root coordinates are not necessarily taskbar coordinates on dual
    // monitors. Use the validated physical Start bounds and primary HWND origin.
    Windows::Foundation::Rect actual{};
    if(!TryPrimaryBounds(start,bounds,actual)){Restore();return 2;}
    double naturalX=(actual.X-bounds.left)*96.0/dpi-(ownsTransform?translation.X():0.0);
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
        translation=Media::TranslateTransform();translation.X(wanted);start.RenderTransform(translation);ownsTransform=true;return 4;
    }
    if(ownsTransform&&std::abs(translation.X()-wanted)>.5){translation.X(wanted);return 4;}
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
        if(owner>0&&heartbeat<=now&&now-heartbeat<2500&&req>0&&req<=1024&&bar&&dpi&&GetWindowRect(bar,&bounds)&&bounds.right-bounds.left>bounds.bottom-bounds.top&&req*dpi/96.0<(bounds.right-bounds.left)*.45){
            w=req;state=SelectPrimary(bounds)?Apply(w,bounds,dpi):2;
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
            if(mutation==Remove){candidates.erase(std::remove_if(candidates.begin(),candidates.end(),[&](auto const& item){return item.first==el.Handle;}),candidates.end());if(el.Handle==startHandle){Restore();start=nullptr;startHandle=0;conflict=false;}return S_OK;}
            // Publish diagnostic heartbeat even when the expected Start element
            // is absent on a newer visual tree. Never operate across UI threads.
            if(!timerId){HMODULE module=nullptr;
                if(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&Tick),&module))timerId=SetTimer(nullptr,0,250,Tick);
            }
            if(mutation!=Add||!el.Type||(wcscmp(el.Type,L"Taskbar.ExperienceToggleButton")!=0&&wcscmp(el.Type,L"Windows.UI.Xaml.Controls.Primitives.ToggleButton")!=0))return S_OK;
            Windows::Foundation::IInspectable obj{nullptr};
            if(FAILED(diagnostics->GetIInspectableFromHandle(el.Handle,reinterpret_cast<::IInspectable**>(put_abi(obj)))))return S_OK;
            auto f=obj.try_as<FrameworkElement>();
            if(!IsStart(f))return S_OK;
            if(std::none_of(candidates.begin(),candidates.end(),[&](auto const& item){return item.first==el.Handle;}))candidates.emplace_back(el.Handle,f);
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
STDAPI DllGetClassObject(REFCLSID clsid,REFIID iid,void** out){
    if(!out)return E_POINTER;*out=nullptr;if(clsid!=SlotClsid||!PrimaryBar())return CLASS_E_CLASSNOTAVAILABLE;
    try{return make<Factory>().as(iid,out);}catch(...){return to_hresult();}
}
STDAPI DllCanUnloadNow(){return S_FALSE;}
