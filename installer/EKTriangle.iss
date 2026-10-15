; EK Triangle - Windows installer (Inno Setup 6)
;
; Installs per user (no administrator rights for the add-in). Only the
; Project Browser step, which changes files under Program Files, is started
; with a UAC prompt - and only when a selected Revit version needs it.
; All file work is done by EKTriangle.exe (src/tool), which is covered by the
; automated tests; this script handles the wizard and sequencing.
;
; Build:  ISCC /DPayload=<folder with EKTriangle.exe and Revit20xx\> installer\EKTriangle.iss
; Silent: EK_Triangle_Setup.exe /VERYSILENT [/YEARS=2024,2025] [/NOBROWSER] [/NONATIVE]

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Payload
  #define Payload "..\out\payload"
#endif
#ifndef OutDir
  #define OutDir "..\out\installer"
#endif

[Setup]
AppId={{6FC522F7-EAC3-427C-9E28-5613722867E8}
AppName=EK Triangle
AppVersion={#AppVersion}
AppVerName=EK Triangle {#AppVersion}
AppPublisher=EK
AppComments=Triangle expand/collapse symbols for Autodesk Revit 2024-2027
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=EK
VersionInfoDescription=EK Triangle Setup
VersionInfoProductName=EK Triangle
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (c) EK
PrivilegesRequired=lowest
DefaultDirName={autopf}\EK Triangle
DisableDirPage=yes
DefaultGroupName=EK Triangle
DisableProgramGroupPage=yes
DisableWelcomePage=yes
DisableReadyPage=yes
UninstallDisplayName=EK Triangle
UninstallDisplayIcon={app}\EKTriangle.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
WizardSizePercent=110
WizardSmallImageFile=WizardSmall.bmp,WizardSmall-2x.bmp
SetupIconFile=EKTriangle.ico
OutputDir={#OutDir}
OutputBaseFilename=EK_Triangle_Setup
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
CloseApplications=no
RestartApplications=no
AllowCancelDuringInstall=no

[Messages]
SetupWindowTitle=EK Triangle %1 Setup
FinishedHeadingLabel=EK Triangle setup finished
UninstalledAll=EK Triangle was removed. Revit's original expand/collapse symbols are back, except for anything listed in a warning before this message.
UninstalledMost=EK Triangle uninstall finished.%n%nSome items could not be removed. They were listed before this message.

[Files]
Source: "{#Payload}\EKTriangle.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\EKTriangle.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\EKTriangle.exe"; Flags: dontcopy
Source: "{#Payload}\Revit2024\*"; DestDir: "{app}\Revit2024"; Flags: ignoreversion
Source: "{#Payload}\Revit2025\*"; DestDir: "{app}\Revit2025"; Flags: ignoreversion
Source: "{#Payload}\Revit2026\*"; DestDir: "{app}\Revit2026"; Flags: ignoreversion
Source: "{#Payload}\Revit2027\*"; DestDir: "{app}\Revit2027"; Flags: ignoreversion
Source: "glyph.ini"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\EK Triangle Restore"; Filename: "{app}\EKTriangle.exe"; Parameters: "restore"; Comment: "Restore Revit's original expand/collapse symbols"

[Code]
const
  YEAR_COUNT = 4;
  EXIT_OK = 0;
  EXIT_PROBLEMS = 1;
  EXIT_BLOCKED = 2;

var
  Years: array[0..3] of Integer;
  Detected: array[0..3] of Boolean;
  ListIndex: array[0..3] of Integer;
  ScanIni: String;
  VersionPage: TWizardPage;
  YearList: TNewCheckListBox;
  NativeCheck, BrowserCheck: TNewCheckBox;
  NoteLabel: TNewStaticText;
  Summary: String;
  UninstallProblems: String;

{ ---------------------------------------------------------------- helpers -- }

function Ini(const Section, Key, FileName: String): String;
begin
  Result := GetIniString(Section, Key, '', FileName);
end;

function HasParam(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/' + Name) = 0 then
      Result := True;
end;

function ParamYears: String;
begin
  Result := ExpandConstant('{param:YEARS|}');
end;

function InList(const CsvList: String; Year: Integer): Boolean;
begin
  Result := Pos(',' + IntToStr(Year) + ',', ',' + CsvList + ',') > 0;
end;

procedure AddTo(var CsvList: String; Year: Integer);
begin
  if CsvList <> '' then
    CsvList := CsvList + ',';
  CsvList := CsvList + IntToStr(Year);
end;

function TempFile(const Name: String): String;
begin
  Result := AddBackslash(GetTempDir) + 'EKTriangle-' + Name + '-' + GetDateTimeString('hhnnsszzz', #0, #0) + '.ini';
end;

{ Runs the helper as the current user. Returns its exit code (or -1). }
function RunTool(const Exe, Params: String): Integer;
var
  Code: Integer;
begin
  Log('EKTriangle.exe ' + Params);
  if Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := Code
  else
    Result := -1;
  Log('  exit code ' + IntToStr(Result));
end;

{ Runs the helper with administrator rights (UAC prompt unless already elevated).
  Returns True when the helper wrote its result file. }
function RunToolElevated(const Exe, Params, OutFile: String): Boolean;
var
  Code: Integer;
begin
  DeleteFile(OutFile);
  Log('EKTriangle.exe (administrator) ' + Params);
  if IsAdmin then
    Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, Code)
  else if not ShellExec('runas', Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Log('  elevation not started, code ' + IntToStr(Code));
  Result := FileExists(OutFile);
  Log('  result file present: ' + IntToStr(Ord(Result)));
end;

function RunningRevit(const Exe: String): String;
var
  F: String;
begin
  Result := '';
  F := TempFile('running');
  if RunTool(Exe, 'running --out "' + F + '"') = EXIT_BLOCKED then
  begin
    Result := Ini('Summary', 'Running', F);
    if Result = '' then
      Result := 'Revit';
  end;
  DeleteFile(F);
end;

{ ------------------------------------------------------------ setup flow -- }

function SetupTool: String;
begin
  Result := ExpandConstant('{tmp}\EKTriangle.exe');
end;

function InitializeSetup: Boolean;
var
  I: Integer;
  Any: Boolean;
begin
  Years[0] := 2024; Years[1] := 2025; Years[2] := 2026; Years[3] := 2027;
  ExtractTemporaryFile('EKTriangle.exe');
  ScanIni := ExpandConstant('{tmp}\scan.ini');
  RunTool(SetupTool, 'scan --out "' + ScanIni + '"');
  Any := False;
  for I := 0 to YEAR_COUNT - 1 do
  begin
    Detected[I] := Ini(IntToStr(Years[I]), 'Revit', ScanIni) = '1';
    if Detected[I] then
    begin
      Any := True;
      Log(Format('Revit %d: add-in %s (%s); Project Browser %s (%s)', [Years[I],
        Ini(IntToStr(Years[I]), 'Native', ScanIni), Ini(IntToStr(Years[I]), 'NativeText', ScanIni),
        Ini(IntToStr(Years[I]), 'Browser', ScanIni), Ini(IntToStr(Years[I]), 'BrowserText', ScanIni)]));
    end;
  end;
  if not Any then
  begin
    SuppressibleMsgBox('EK Triangle did not find Autodesk Revit 2024, 2025, 2026 or 2027 on this computer.' + #13#10#13#10 +
      'Install Revit first, then run this setup again.', mbInformation, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  Result := True;
end;

function NativeCode(I: Integer): String;
begin
  Result := Ini(IntToStr(Years[I]), 'Native', ScanIni);
end;

function BrowserCode(I: Integer): String;
begin
  Result := Ini(IntToStr(Years[I]), 'Browser', ScanIni);
end;

function NativePossible(I: Integer): Boolean;
begin
  Result := (NativeCode(I) = 'Ready') or (NativeCode(I) = 'Installed');
end;

function BrowserPossible(I: Integer): Boolean;
begin
  Result := (BrowserCode(I) = 'Ready') or (BrowserCode(I) = 'Applied');
end;

function YearLine(I: Integer): String;
var
  N, B: String;
begin
  if NativePossible(I) then N := 'dialogs: ready' else N := 'dialogs: not available';
  if NativeCode(I) = 'Installed' then N := 'dialogs: installed';
  if BrowserCode(I) = 'Ready' then B := 'Project Browser: compatible'
  else if BrowserCode(I) = 'Applied' then B := 'Project Browser: installed'
  else B := 'Project Browser: not supported, will be left unchanged';
  Result := N + ',  ' + B;
end;

procedure InitializeWizard;
var
  I, Idx: Integer;
  Legacy, Notes: String;
begin
  VersionPage := CreateCustomPage(wpWelcome, 'Choose Revit versions',
    'EK Triangle replaces the + and - expand/collapse symbols with small dark-gray triangles.');

  YearList := TNewCheckListBox.Create(VersionPage);
  YearList.Parent := VersionPage.Surface;
  YearList.Left := 0;
  YearList.Top := 0;
  YearList.Width := VersionPage.SurfaceWidth;
  YearList.Height := ScaleY(112);
  YearList.Flat := True;
  for I := 0 to YEAR_COUNT - 1 do
  begin
    ListIndex[I] := -1;
    if Detected[I] then
    begin
      Idx := YearList.AddCheckBox('Revit ' + IntToStr(Years[I]), YearLine(I), 0,
        NativePossible(I) or BrowserPossible(I), NativePossible(I) or BrowserPossible(I), False, True, nil);
      ListIndex[I] := Idx;
      if NativeCode(I) = 'Conflict' then
        Notes := Notes + 'Revit ' + IntToStr(Years[I]) + ': ' + Ini(IntToStr(Years[I]), 'NativeText', ScanIni) + #13#10;
      Legacy := Ini(IntToStr(Years[I]), 'Legacy', ScanIni);
      if (Legacy <> '') and (NativeCode(I) <> 'Conflict') then
        Notes := Notes + 'Revit ' + IntToStr(Years[I]) + ': the earlier EKGlyph test add-in was found. It will be turned off ' +
          '(kept as a backup) so that only one version runs.' + #13#10;
    end;
  end;

  NativeCheck := TNewCheckBox.Create(VersionPage);
  NativeCheck.Parent := VersionPage.Surface;
  NativeCheck.Top := YearList.Top + YearList.Height + ScaleY(10);
  NativeCheck.Width := VersionPage.SurfaceWidth;
  NativeCheck.Caption := 'Triangles in Revit dialogs (Visibility/Graphics and other lists)';
  NativeCheck.Checked := not HasParam('NONATIVE');

  BrowserCheck := TNewCheckBox.Create(VersionPage);
  BrowserCheck.Parent := VersionPage.Surface;
  BrowserCheck.Top := NativeCheck.Top + ScaleY(22);
  BrowserCheck.Width := VersionPage.SurfaceWidth;
  BrowserCheck.Caption := 'Triangles in the Project Browser (views and sheets)';
  BrowserCheck.Checked := not HasParam('NOBROWSER');

  NoteLabel := TNewStaticText.Create(VersionPage);
  NoteLabel.Parent := VersionPage.Surface;
  NoteLabel.Top := BrowserCheck.Top + ScaleY(28);
  NoteLabel.Width := VersionPage.SurfaceWidth;
  NoteLabel.AutoSize := False;
  NoteLabel.Height := VersionPage.SurfaceHeight - NoteLabel.Top;
  NoteLabel.WordWrap := True;
  NoteLabel.Caption := Notes +
    'The Project Browser option changes Revit''s own resource files after making a verified backup, so Windows will ' +
    'ask for administrator permission. Unrecognized Revit versions are skipped automatically.' + #13#10 +
    'Please close Revit before you continue.';
end;

function YearSelected(I: Integer): Boolean;
begin
  if not Detected[I] then
    Result := False
  else if WizardSilent then
  begin
    if ParamYears <> '' then
      Result := InList(ParamYears, Years[I])
    else
      Result := NativePossible(I) or BrowserPossible(I);
  end
  else
    Result := YearList.Checked[ListIndex[I]];
end;

function CheckRevitClosed(const Exe: String): Boolean;
var
  Running: String;
begin
  Result := True;
  Running := RunningRevit(Exe);
  while Running <> '' do
  begin
    if SuppressibleMsgBox('Please close ' + Running + ' first (save your work), then click Retry.',
         mbError, MB_RETRYCANCEL, IDCANCEL) <> IDRETRY then
    begin
      Result := False;
      Exit;
    end;
    Running := RunningRevit(Exe);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  I: Integer;
  Any: Boolean;
begin
  Result := True;
  if CurPageID = VersionPage.ID then
  begin
    Any := False;
    for I := 0 to YEAR_COUNT - 1 do
      if YearSelected(I) then Any := True;
    if not Any then
    begin
      SuppressibleMsgBox('Select at least one Revit version.', mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
    if not NativeCheck.Checked and not BrowserCheck.Checked then
    begin
      SuppressibleMsgBox('Select at least one of the two options.', mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
    Result := CheckRevitClosed(SetupTool);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Running: String;
begin
  Result := '';
  Running := RunningRevit(SetupTool);
  if Running <> '' then
    Result := 'Please close ' + Running + ' and run EK Triangle Setup again. Nothing was changed.';
end;

function Describe(const Code, Text: String): String;
begin
  if (Code = 'Done') or (Code = 'AlreadyDone') then
    Result := 'installed'
  else if Code = 'Removed' then
    Result := 'removed'
  else if Code = 'Restored' then
    Result := 'original restored'
  else if Code = 'Skipped' then
    Result := 'left unchanged (' + Text + ')'
  else if Code = '' then
    Result := ''
  else
    Result := 'NOT done - ' + Text;
end;

procedure DoInstall;
var
  I: Integer;
  App, Exe, NativeYears, RemoveYears, BrowserYears, RestoreYears, NativeIni, BrowserIni, Y, Line, N, B: String;
  BrowserRan, Problems: Boolean;
begin
  App := ExpandConstant('{app}');
  Exe := App + '\EKTriangle.exe';
  for I := 0 to YEAR_COUNT - 1 do
    if Detected[I] then
    begin
      if YearSelected(I) and NativeCheck.Checked and NativePossible(I) then
        AddTo(NativeYears, Years[I])
      else if NativeCode(I) = 'Installed' then
        AddTo(RemoveYears, Years[I]);
      if YearSelected(I) and BrowserCheck.Checked and (BrowserCode(I) = 'Ready') then
        AddTo(BrowserYears, Years[I])
      else if (BrowserCode(I) = 'Applied') and not (YearSelected(I) and BrowserCheck.Checked) then
        AddTo(RestoreYears, Years[I]);
    end;

  NativeIni := TempFile('native');
  BrowserIni := TempFile('browser');
  if (NativeYears <> '') or (RemoveYears <> '') then
  begin
    WizardForm.StatusLabel.Caption := 'Installing the add-in for Revit dialogs...';
    RunTool(Exe, 'install --years "' + NativeYears + '" --remove-years "' + RemoveYears + '" --payload "' + App + '" --out "' + NativeIni + '"');
  end;
  BrowserRan := True;
  if (BrowserYears <> '') or (RestoreYears <> '') then
  begin
    WizardForm.StatusLabel.Caption := 'Updating the Project Browser (administrator permission)...';
    BrowserRan := RunToolElevated(Exe, 'browser-apply --years "' + BrowserYears + '" --restore-years "' + RestoreYears + '" --out "' + BrowserIni + '"', BrowserIni);
  end;

  Problems := False;
  Summary := '';
  for I := 0 to YEAR_COUNT - 1 do
    if Detected[I] then
    begin
      Y := IntToStr(Years[I]);
      N := Describe(Ini(Y, 'NativeResult', NativeIni), Ini(Y, 'NativeResultText', NativeIni));
      if (N = '') and (NativeCode(I) = 'Conflict') and YearSelected(I) and NativeCheck.Checked then
        N := 'left unchanged (' + Ini(Y, 'NativeText', ScanIni) + ')';
      if InList(BrowserYears, Years[I]) or InList(RestoreYears, Years[I]) then
      begin
        if BrowserRan then
          B := Describe(Ini(Y, 'BrowserResult', BrowserIni), Ini(Y, 'BrowserResultText', BrowserIni))
        else
          B := 'NOT changed - administrator permission was not given';
      end
      else if YearSelected(I) and BrowserCheck.Checked and (BrowserCode(I) = 'Applied') then
        B := 'installed'
      else if YearSelected(I) and BrowserCheck.Checked then
        B := 'left unchanged (' + Ini(Y, 'BrowserText', ScanIni) + ')'
      else
        B := '';
      if (Pos('NOT ', N) = 1) or (Pos('NOT ', B) = 1) then Problems := True;
      Line := '';
      if N <> '' then Line := 'dialogs ' + N;
      if B <> '' then
      begin
        if Line <> '' then Line := Line + '; ';
        Line := Line + 'Project Browser ' + B;
      end;
      if Line <> '' then
        Summary := Summary + 'Revit ' + Y + ': ' + Line + '.' + #13#10;
    end;
  Log('Summary:' + #13#10 + Summary);
  if Problems then
    Summary := Summary + #13#10 + 'Some items were not completed; Revit was left in its previous working state for those items. ' +
      'You can run this setup again at any time.' + #13#10;
  Summary := Summary + #13#10 + 'Start Revit. If Revit asks whether to load "EK Triangle", choose "Always Load".' + #13#10 +
    'To undo everything: Windows Settings > Apps > Installed apps > EK Triangle > Uninstall.';
  DeleteFile(NativeIni);
  DeleteFile(BrowserIni);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    DoInstall;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = VersionPage.ID then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedLabel.AutoSize := False;
    WizardForm.FinishedLabel.Height := WizardForm.FinishedPage.Height - WizardForm.FinishedLabel.Top - ScaleY(8);
    WizardForm.FinishedLabel.Caption := Summary;
  end;
end;

{ -------------------------------------------------------------- uninstall -- }

function UninstallTool: String;
begin
  Result := ExpandConstant('{app}\EKTriangle.exe');
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if not FileExists(UninstallTool) then
    Exit;
  Result := CheckRevitClosed(UninstallTool);
end;

procedure RestoreBrowser;
var
  ScanFile, OutFile, Needs, Unresolved: String;
  Done: Boolean;
begin
  ScanFile := TempFile('uscan');
  RunTool(UninstallTool, 'scan --out "' + ScanFile + '"');
  Needs := Ini('Summary', 'NeedsBrowserRestore', ScanFile);
  DeleteFile(ScanFile);
  if Needs = '' then
    Exit;
  OutFile := TempFile('restore');
  Done := False;
  while not Done do
  begin
    if RunToolElevated(UninstallTool, 'browser-restore --out "' + OutFile + '"', OutFile) then
    begin
      Done := True;
      Unresolved := Ini('Summary', 'Unresolved', OutFile);
      if Ini('Summary', 'Result', OutFile) = 'Blocked' then
        Unresolved := Ini('Summary', 'Message', OutFile)
      else if (Ini('Summary', 'Result', OutFile) <> 'OK') and (Unresolved = '') then
        Unresolved := Ini('Summary', 'Message', OutFile);
      if Unresolved <> '' then
      begin
        StringChangeEx(Unresolved, ' || ', #13#10, True);
        UninstallProblems := 'The original Project Browser files could NOT be restored for:' + #13#10 + Unresolved + #13#10;
      end;
    end
    else if SuppressibleMsgBox('Administrator permission is needed to restore Revit''s original Project Browser files (' + Needs + ').' + #13#10#13#10 +
      'Click Retry to ask again, or Cancel to finish uninstalling without restoring them.',
      mbError, MB_RETRYCANCEL, IDCANCEL) <> IDRETRY then
    begin
      Done := True;
      UninstallProblems := 'The original Project Browser files were NOT restored for Revit ' + Needs +
        ' (administrator permission was not given).' + #13#10;
    end;
  end;
  DeleteFile(OutFile);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  OutFile: String;
  Code: Integer;
begin
  if (CurUninstallStep = usUninstall) and FileExists(UninstallTool) then
  begin
    OutFile := TempFile('uninstall');
    Code := RunTool(UninstallTool, 'uninstall --out "' + OutFile + '"');
    if Code <> EXIT_OK then
      UninstallProblems := UninstallProblems + 'Some Revit add-in files could not be removed: ' +
        Ini('Summary', 'Message', OutFile) + #13#10;
    DeleteFile(OutFile);
    RestoreBrowser;
  end;
  if (CurUninstallStep = usPostUninstall) and (UninstallProblems <> '') then
    SuppressibleMsgBox(UninstallProblems + #13#10 +
      'Nothing unknown was overwritten. The original backups are kept in C:\ProgramData\EKTriangle. ' +
      'To restore later, close Revit and run "C:\ProgramData\EKTriangle\EK Triangle Restore.exe".',
      mbError, MB_OK, IDOK);
end;
