; EK FastPDF - Windows installer (Inno Setup 6)
;
; Installs the self-contained application (its own .NET 8 runtime and PDFium; nothing else is
; downloaded or required) into Program Files, with Start menu entry, optional desktop shortcut,
; optional "Open with" registration for PDF files, Apps & features uninstall and in-place
; upgrades (same AppId). It never changes the user's default PDF application: Windows only
; allows the user to choose that, in Settings or in the "Open with" dialog.
;
; Build:  ISCC /DPublishDir=<dotnet publish folder> /DAppVersion=1.2.0 installer\EKFastPDF.iss
; Silent: EK_FastPDF_Setup_v1.2.exe /VERYSILENT [/TASKS="desktopicon,pdfopenwith"] [/CURRENTUSER]

#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\out\publish\EKFastPDF"
#endif
#ifndef OutDir
  #define OutDir "..\out\installer"
#endif
#ifndef OutName
  #define OutName "EK_FastPDF_Setup_v1.2"
#endif

#define ProgId "EKFastPDF.Document"
#define CapsKey "Software\EK\EK FastPDF\Capabilities"

[Setup]
AppId={{31680A75-F231-4280-A95E-9D3AF023FD36}
AppName=EK FastPDF
AppVersion={#AppVersion}
AppVerName=EK FastPDF {#AppVersion}
AppPublisher=EK
AppComments=Fast PDF viewer for large drawings
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=EK
VersionInfoDescription=EK FastPDF Setup
VersionInfoProductName=EK FastPDF
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (c) EK
; Program Files by default (administrator); "install for me only" is offered as an alternative.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\EK FastPDF
DefaultGroupName=EK FastPDF
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UninstallDisplayName=EK FastPDF
UninstallDisplayIcon={app}\EKFastPDF.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 or later (WPF on .NET 8 requirement).
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=FastPDF.ico
OutputDir={#OutDir}
OutputBaseFilename={#OutName}
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
; Upgrades: close a running EK FastPDF (viewer and its render workers) via Restart Manager.
CloseApplications=force
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
ChangesAssociations=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "pdfopenwith"; Description: "Offer EK FastPDF in ""Open with"" for PDF files (does not change your default PDF app)"; GroupDescription: "PDF files:"

[InstallDelete]
; Remove the previous version's runtime and libraries before copying the new set, so an upgrade
; never mixes assemblies from two builds. User settings live in %APPDATA% and are kept.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"
Type: filesandordirs; Name: "{app}\runtimes"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\EK FastPDF"; Filename: "{app}\EKFastPDF.exe"; WorkingDir: "{app}"; Comment: "Open PDF documents and drawings"
Name: "{autodesktop}\EK FastPDF"; Filename: "{app}\EKFastPDF.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; ProgID used by "Open with" and by Default Apps if the user picks EK FastPDF.
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "PDF Document"; Flags: uninsdeletekey; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "PDF Document"; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\EKFastPDF.exe"",0"; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\EKFastPDF.exe"" ""%1"""; Tasks: pdfopenwith
; Listed under "Open with" for .pdf; the default handler (UserChoice) is left untouched.
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\Applications\EKFastPDF.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "EK FastPDF"; Flags: uninsdeletekey; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\Applications\EKFastPDF.exe\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\Classes\Applications\EKFastPDF.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\EKFastPDF.exe"" ""%1"""; Tasks: pdfopenwith
; Appears in Settings > Apps > Default apps so the user can choose it there.
Root: HKA; Subkey: "{#CapsKey}"; ValueType: string; ValueName: "ApplicationName"; ValueData: "EK FastPDF"; Flags: uninsdeletekey; Tasks: pdfopenwith
Root: HKA; Subkey: "{#CapsKey}"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Fast PDF viewer for large drawings"; Tasks: pdfopenwith
Root: HKA; Subkey: "{#CapsKey}\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "{#ProgId}"; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "EK FastPDF"; ValueData: "{#CapsKey}"; Flags: uninsdeletevalue; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\EK\EK FastPDF"; Flags: uninsdeletekeyifempty; Tasks: pdfopenwith
Root: HKA; Subkey: "Software\EK"; Flags: uninsdeletekeyifempty; Tasks: pdfopenwith
; App Paths: lets "Win+R > EKFastPDF" and other launchers find the program.
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\EKFastPDF.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\EKFastPDF.exe"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\EKFastPDF.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"

[Run]
; Started as the original (non-elevated) user, the same way a Start menu click starts it.
Filename: "{app}\EKFastPDF.exe"; Description: "Start EK FastPDF"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Stop running copies (viewer + render workers) so every file can be removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM EKFastPDF.exe"; Flags: runhidden; RunOnceId: "StopEKFastPDF"
