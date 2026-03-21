#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef OutputFilename
  #define OutputFilename "AutoWhisper-Setup"
#endif

[Setup]
AppName=AutoWhisper
AppVersion={#AppVersion}
AppVerName=AutoWhisper
AppPublisher=DNO Development
AppPublisherURL=https://github.com/dnodevelopment
DefaultDirName={commonpf64}\AutoWhisper
DefaultGroupName=AutoWhisper
UninstallDisplayIcon={app}\AutoWhisper.exe
OutputDir=installer-output
OutputBaseFilename={#OutputFilename}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=src\AutoWhisper\Assets\app-icon.ico
PrivilegesRequired=admin
WizardStyle=modern
CloseApplications=force

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Models"
Type: filesandordirs; Name: "{app}\runtimes"
Type: files; Name: "{app}\settings.json"
Type: files; Name: "{app}\autowhisper.log"

[Icons]
Name: "{group}\AutoWhisper"; Filename: "{app}\AutoWhisper.exe"
Name: "{group}\Uninstall AutoWhisper"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AutoWhisper"; Filename: "{app}\AutoWhisper.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupicon"; Description: "Launch AutoWhisper at Windows startup"; GroupDescription: "Startup:"
Name: "runasadmin"; Description: "Run as administrator (required to paste text into admin applications)"; GroupDescription: "Permissions:"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AutoWhisper"; ValueData: """{app}\AutoWhisper.exe"""; Flags: uninsdeletevalue; Tasks: startupicon
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; ValueType: string; ValueName: "{app}\AutoWhisper.exe"; ValueData: "~ RUNASADMIN"; Flags: uninsdeletevalue; Tasks: runasadmin

[Run]
Filename: "{app}\AutoWhisper.exe"; Parameters: "--show-settings"; Description: "Launch AutoWhisper"; Flags: shellexec nowait postinstall skipifsilent

[Code]
var
  InstallTypePage: TInputOptionWizardPage;
  ModelPage: TInputOptionWizardPage;
  LanguageCustomPage: TWizardPage;
  LanguageCombo: TNewComboBox;
  DownloadPage: TDownloadWizardPage;
  ExistingInstallDetected: Boolean;

function IsUpgradeInstall: Boolean;
begin
  Result := ExistingInstallDetected and (InstallTypePage.SelectedValueIndex = 0);
end;

function GetExistingUninstallString: String;
var
  UninstallKey: String;
begin
  Result := '';
  UninstallKey := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AutoWhisper_is1';
  if not RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', Result) then
    RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', Result);
end;

function GetModelFileName(Index: Integer): String;
begin
  case Index of
    0: Result := 'ggml-tiny.bin';
    1: Result := 'ggml-base.bin';
    2: Result := 'ggml-small.bin';
    3: Result := 'ggml-medium.bin';
    4: Result := 'ggml-large-v3.bin';
  else
    RaiseException('Invalid model index: ' + IntToStr(Index));
  end;
end;

function GetModelName(Index: Integer): String;
begin
  case Index of
    0: Result := 'tiny';
    1: Result := 'base';
    2: Result := 'small';
    3: Result := 'medium';
    4: Result := 'large-v3';
  else
    RaiseException('Invalid model index: ' + IntToStr(Index));
  end;
end;

function GetModelUrl(Index: Integer): String;
begin
  Result := 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/' + GetModelFileName(Index);
end;

function GetLanguageCode(Index: Integer): String;
begin
  case Index of
    0: Result := 'auto';
    1: Result := 'en';
    2: Result := 'nl';
    3: Result := 'fr';
    4: Result := 'de';
    5: Result := 'es';
    6: Result := 'it';
    7: Result := 'pt';
    8: Result := 'ru';
    9: Result := 'zh';
    10: Result := 'ja';
    11: Result := 'ko';
    12: Result := 'ar';
    13: Result := 'hi';
    14: Result := 'pl';
    15: Result := 'tr';
    16: Result := 'uk';
    17: Result := 'sv';
    18: Result := 'da';
    19: Result := 'no';
    20: Result := 'fi';
    21: Result := 'cs';
    22: Result := 'ro';
    23: Result := 'hu';
    24: Result := 'el';
    25: Result := 'he';
    26: Result := 'th';
    27: Result := 'vi';
    28: Result := 'id';
  else
    Result := 'auto';
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax <> 0 then
    Log(Format('Download progress: %d of %d bytes', [Progress, ProgressMax]));
  Result := True;
end;

procedure DownloadModelToDir(ModelIndex: Integer; const ModelDir: String);
var
  TempFile: String;
  DestFile: String;
begin
  DestFile := ModelDir + '\' + GetModelFileName(ModelIndex);
  TempFile := ExpandConstant('{tmp}\') + GetModelFileName(ModelIndex);

  DownloadPage.Clear;
  DownloadPage.Add(GetModelUrl(ModelIndex), GetModelFileName(ModelIndex), '');
  DownloadPage.Show;
  try
    DownloadPage.Download;

    if not FileExists(TempFile) then
    begin
      MsgBox('Download failed: temporary file not found.' + #13#10 +
             'Please check your internet connection and re-run setup.', mbError, MB_OK);
      Abort;
    end;

    if not CopyFile(TempFile, DestFile, False) then
    begin
      MsgBox('Failed to copy model to: ' + DestFile + #13#10 +
             'Ensure you have sufficient disk space and write permissions.', mbError, MB_OK);
      DeleteFile(TempFile);
      Abort;
    end;

    DeleteFile(TempFile);
  finally
    DownloadPage.Hide;
  end;
end;

procedure WriteSettings(ModelIndex, LangIndex: Integer);
var
  SettingsFile: String;
  SettingsJson: String;
  LaunchAtStartup: String;
begin
  SettingsFile := ExpandConstant('{app}\settings.json');

  if FileExists(SettingsFile) then
    Exit;

  if WizardIsTaskSelected('startupicon') then
    LaunchAtStartup := 'true'
  else
    LaunchAtStartup := 'false';

  SettingsJson := '{' + #13#10;
  SettingsJson := SettingsJson + '  "HotkeyKey": "VcSpace",' + #13#10;
  SettingsJson := SettingsJson + '  "HotkeyModifiers": "LeftCtrl, LeftShift",' + #13#10;
  SettingsJson := SettingsJson + '  "SelectedModel": "' + GetModelName(ModelIndex) + '",' + #13#10;
  SettingsJson := SettingsJson + '  "Language": "' + GetLanguageCode(LangIndex) + '",' + #13#10;
  SettingsJson := SettingsJson + '  "LaunchAtStartup": ' + LaunchAtStartup + ',' + #13#10;
  SettingsJson := SettingsJson + '  "SelectedMicrophone": ""' + #13#10;
  SettingsJson := SettingsJson + '}';

  if not SaveStringToFile(SettingsFile, SettingsJson, False) then
    MsgBox('Failed to write settings file: ' + SettingsFile + #13#10 +
           'AutoWhisper will use default settings on first launch.', mbError, MB_OK);
end;

procedure RunExistingUninstaller;
var
  UninstallString: String;
  ResultCode: Integer;
begin
  UninstallString := GetExistingUninstallString;
  if UninstallString <> '' then
  begin
    // Remove surrounding quotes if present
    if (Length(UninstallString) > 1) and (UninstallString[1] = '"') then
      UninstallString := Copy(UninstallString, 2, Length(UninstallString) - 2);

    Log('Running existing uninstaller: ' + UninstallString);
    Exec(UninstallString, '/SILENT /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Log('Uninstaller exited with code: ' + IntToStr(ResultCode));
    Sleep(1000);
  end;
end;

procedure InitializeWizard;
var
  DescLabel: TNewStaticText;
begin
  // Install type page — only meaningful when existing install is detected
  InstallTypePage := CreateInputOptionPage(wpWelcome,
    'Existing Installation Detected',
    'AutoWhisper is already installed on this computer.',
    'Choose how you would like to proceed:',
    True, False);
  InstallTypePage.Add('Update — Keep my settings and downloaded models');
  InstallTypePage.Add('Clean Install — Remove everything and start fresh');
  InstallTypePage.SelectedValueIndex := 0;

  // Model selection page (radio buttons)
  ModelPage := CreateInputOptionPage(wpSelectTasks,
    'Select Whisper Model',
    'Choose the speech recognition model to download.',
    'Larger models are more accurate but slower and use more disk space.'#13#10 +
    'All models support 28 languages including English, Dutch, French, German, and more.',
    True, False);
  ModelPage.Add('Tiny (39 MB) - Fastest, least accurate');
  ModelPage.Add('Base (142 MB) - Fast, good accuracy');
  ModelPage.Add('Small (466 MB) - Balanced speed and accuracy');
  ModelPage.Add('Medium (1.5 GB) - Slow, very accurate');
  ModelPage.Add('Large v3 (3.1 GB) - Slowest, best accuracy');
  ModelPage.SelectedValueIndex := 2;

  // Language selection page (dropdown)
  LanguageCustomPage := CreateCustomPage(ModelPage.ID,
    'Select Language',
    'Choose the language for speech recognition.');

  DescLabel := TNewStaticText.Create(LanguageCustomPage);
  DescLabel.Parent := LanguageCustomPage.Surface;
  DescLabel.Left := 0;
  DescLabel.Top := 0;
  DescLabel.Width := LanguageCustomPage.SurfaceWidth;
  DescLabel.WordWrap := True;
  DescLabel.Caption := 'Select "Auto-detect" to let the model identify the spoken language automatically. ' +
    'For best results with a specific language, select it explicitly.';

  LanguageCombo := TNewComboBox.Create(LanguageCustomPage);
  LanguageCombo.Parent := LanguageCustomPage.Surface;
  LanguageCombo.Left := 0;
  LanguageCombo.Top := DescLabel.Top + DescLabel.Height + 16;
  LanguageCombo.Width := 300;
  LanguageCombo.Style := csDropDownList;
  LanguageCombo.Items.Add('Auto-detect');
  LanguageCombo.Items.Add('English');
  LanguageCombo.Items.Add('Dutch');
  LanguageCombo.Items.Add('French');
  LanguageCombo.Items.Add('German');
  LanguageCombo.Items.Add('Spanish');
  LanguageCombo.Items.Add('Italian');
  LanguageCombo.Items.Add('Portuguese');
  LanguageCombo.Items.Add('Russian');
  LanguageCombo.Items.Add('Chinese');
  LanguageCombo.Items.Add('Japanese');
  LanguageCombo.Items.Add('Korean');
  LanguageCombo.Items.Add('Arabic');
  LanguageCombo.Items.Add('Hindi');
  LanguageCombo.Items.Add('Polish');
  LanguageCombo.Items.Add('Turkish');
  LanguageCombo.Items.Add('Ukrainian');
  LanguageCombo.Items.Add('Swedish');
  LanguageCombo.Items.Add('Danish');
  LanguageCombo.Items.Add('Norwegian');
  LanguageCombo.Items.Add('Finnish');
  LanguageCombo.Items.Add('Czech');
  LanguageCombo.Items.Add('Romanian');
  LanguageCombo.Items.Add('Hungarian');
  LanguageCombo.Items.Add('Greek');
  LanguageCombo.Items.Add('Hebrew');
  LanguageCombo.Items.Add('Thai');
  LanguageCombo.Items.Add('Vietnamese');
  LanguageCombo.Items.Add('Indonesian');
  LanguageCombo.ItemIndex := 0;

  // Download page with cancel button
  DownloadPage := CreateDownloadPage(
    'Downloading Model',
    'Please wait while the speech recognition model is downloaded...',
    @OnDownloadProgress);
end;

function InitializeSetup: Boolean;
begin
  // Detect existing installation
  ExistingInstallDetected := GetExistingUninstallString <> '';
  Result := True;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  // Skip install type page when no existing installation
  if PageID = InstallTypePage.ID then
    Result := not ExistingInstallDetected;

  // Skip model, language, and task pages on upgrade
  if IsUpgradeInstall then
  begin
    if (PageID = ModelPage.ID) or (PageID = LanguageCustomPage.ID) or (PageID = wpSelectTasks) then
      Result := True;
  end;
end;

function KillAutoWhisper: Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM AutoWhisper.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Wait for process to fully release file handles
  Sleep(1000);
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillAutoWhisper;

  // For clean install, run the existing uninstaller first
  if ExistingInstallDetected and not IsUpgradeInstall then
    RunExistingUninstaller;

  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDir: String;
begin
  if CurUninstallStep = usUninstall then
    KillAutoWhisper;

  if CurUninstallStep = usPostUninstall then
  begin
    AppDir := ExpandConstant('{app}');
    if DirExists(AppDir) then
      DelTree(AppDir, True, True, True);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ModelDir: String;
begin
  if CurStep = ssPostInstall then
  begin
    // On upgrade, skip model download and settings — keep existing files
    if IsUpgradeInstall then
      Exit;

    ModelDir := ExpandConstant('{app}\Models');
    if not ForceDirectories(ModelDir) then
    begin
      MsgBox('Failed to create model directory: ' + ModelDir + #13#10 +
             'Check that the installation path is writable.', mbError, MB_OK);
      Abort;
    end;

    DownloadModelToDir(ModelPage.SelectedValueIndex, ModelDir);
    WriteSettings(ModelPage.SelectedValueIndex, LanguageCombo.ItemIndex);
  end;
end;
