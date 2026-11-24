# Rings.8 review

Removed: BUILD-INSTALLER.cmd, SETUP-ALL.cmd, SETUP-RINGS.ps1, publish.py, publish.ps1, LGSTraySetup, two Framedep publish profiles, four superseded workflows, NativeLayout Windhawk source, bridge IPC. Previous checklists consolidated into Docs/CHANGE-HISTORY.md.

Retained: upstream copyright/license, HID worker and DLL, actual UI/core/primitives, data providers and tests, MQTT/HTTP functionality, hardware diagnostics. Some legacy resources are still referenced and are retained rather than removed speculatively.

Static checks: XML parsing, project reference existence, solution project file existence, workflow YAML parsing, ZIP integrity. No Windows SDK/desktop available here, so compilation, tests, portable start on clean Windows, actual native left displacement and hardware validation have not been performed. Native left displacement is not implemented; safe overlay only.
