# NativeLayout.dll — experimental Windows shell provider

This C++/WinRT provider is bundled with the portable application. It uses the Windows SDK XAML Diagnostics interface (`InitializeXamlDiagnosticsEx`, `IXamlDiagnostics`, `IVisualTreeService`); it does not require Windhawk or any installed mod. The API loads a task access provider into the primary Explorer process. The taskbar visual-tree element names and layout behavior are internal, not a stable Windows extension API. No Explorer file, registry setting, or instruction bytes are changed.

The provider validates the primary taskbar UI thread and Start button identity, uses a positive trailing margin to change the native repeater's measured width, and attempts to translate Start back to the leading edge in centered mode. Original local properties are recorded and restored when the request is released, a conflict is detected, or the heartbeat becomes older than 2.5 seconds. It does not overwrite unknown transforms or local expressions. The managed process independently checks actual UI Automation screen rectangles before painting the ring strip. If the layout cannot be verified, the application first tries a verified left-side gap, then a safe right-side gap (or hides when none exists). After a persistent conflict it releases the reservation instead of leaving an unnecessary gap.

The first revision is guarded to Windows 11 x64 builds 22621, 22631, 26100, and 26200, one monitor, horizontal taskbar; these are intended test targets, NOT validated compatibility claims. Centered Start displacement, widgets/search controls, diagnostics-session conflicts, overflow, Explorer restarts and differing XAML root structures need real Windows verification. An external diagnostics attach may be refused by Windows; no alternative injection path is attempted. A DLL copy in local app data is pinned for the Explorer process lifetime to avoid dangling UI callbacks; normal exit releases the layout lease, not necessarily the DLL mapping. Restart Explorer to unload it before testing a different native provider build. No elevated operation is needed at runtime. Other XAML diagnostics tools may conflict; do not run simultaneously while testing.

## Build

`BUILD-RINGS.cmd` calls the native build internally. The source build needs MSVC x64 and Windows SDK C++/WinRT headers, in addition to .NET SDK. A precompiled portable release bundles NativeLayout.dll with the static C++ runtime. No end-user tool installation is required. The public source does not include compiled binaries.

## References

- Microsoft XAML Diagnostics SDK: https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nf-xamlom-initializexamldiagnosticsex
- Microsoft visual tree callback: https://learn.microsoft.com/en-us/windows/win32/api/xamlom/nf-xamlom-ivisualtreeservicecallback-onvisualtreechange
- m417z's taskbar layout research (GPL-3.0): https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-start-button-position.wh.cpp — reference for StartButton and TaskbarFrameRepeater identifiers; no Windhawk engine code included.

## Test matrix before release

- Compile on Windows with `BUILD-RINGS.cmd -Test`; check NativeLayout.dll exists in portable archive.
- Disposable Windows VM first: test 25H2, native left and centered alignment, Widgets/Search on and off, 0–4 devices, DPI 100/125/150%, and crowded taskbar; confirm actual UI Automation rectangles and mouse hit testing, not merely preview.
- Switch Left → Right, disconnect all devices, normal exit, force termination, and Explorer restart. Verify original margins and Start position return, with no lingering blank space or desktop crashes.
- Concurrent XAML diagnostics tool, unsupported Windows build, and no safe slot: verify fallback, no native overlap, and honest status text.
- Launch prebuilt portable archive on clean Windows without SDK, .NET Desktop Runtime or Windhawk.
