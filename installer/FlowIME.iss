#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\artifacts\installer"
#endif

#define AppName "FlowIME"
#define AppExeName "FlowIME.App.exe"
#define AppPublisher "FlowIME"
#define AppUrl "https://github.com/MAXR1K3/FlowIME"
#define AppId "{{5DEE174C-19D1-4307-A1F9-7B695DB88885}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\FlowIME
DefaultGroupName=FlowIME
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
MinVersion=10.0.22000
OutputDir={#InstallerOutputDir}
OutputBaseFilename=FlowIME-Setup-{#AppVersion}-x64
SetupIconFile=..\src\FlowIME.App\Assets\Brand\Generated\FlowIME.ico
UninstallDisplayIcon={app}\Assets\Brand\Generated\FlowIME.ico
UninstallDisplayName=FlowIME
WizardStyle=modern light windows11 includetitlebar hidebevels
WizardSizePercent=120
DefaultDialogFontName=Segoe UI
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
UsePreviousLanguage=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=FlowIME 安装程序
VersionInfoProductName=FlowIME
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=FlowIME contributors

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\FlowIME.App\Assets\Brand\Generated\FlowIME.Mark.44.png"; Flags: dontcopy

[Icons]
Name: "{group}\FlowIME"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FlowIME"; ValueData: """{app}\{#AppExeName}"" --background"; Flags: uninsdeletevalue; Check: ShouldEnableStartup

[Code]
const
  RunKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Run';
  UninstallKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1';
  DataDirectoryName = 'FlowIME';

var
  SettingsPage: TWizardPage;
  FolderEdit: TNewEdit;
  BrowseButton: TNewButton;
  StartupCheckBox: TNewCheckBox;
  SettingsLogo: TBitmapImage;
  FinishedLogo: TBitmapImage;
  SettingsTitle: TNewStaticText;
  SettingsSubtitle: TNewStaticText;
  SettingsSectionTitle: TNewStaticText;
  SettingsDescription: TNewStaticText;
  PathLabel: TNewStaticText;
  InstallScopeLabel: TNewStaticText;
  DataRetentionLabel: TNewStaticText;
  VersionLabel: TNewStaticText;
  InstallingTitle: TNewStaticText;
  InstallingHint: TNewStaticText;
  FinishedTitle: TNewStaticText;
  FinishedSubtitle: TNewStaticText;
  FinishedBrandTitle: TNewStaticText;
  FinishedBrandSubtitle: TNewStaticText;
  SuccessMark: TNewStaticText;
  LaunchOnFinish: Boolean;

procedure ConfigureTextLabel(LabelControl: TNewStaticText; AParent: TWinControl;
  const Caption: String; ATop, AHeight, AFontSize: Integer; ABold: Boolean);
begin
  LabelControl.Parent := AParent;
  LabelControl.Caption := Caption;
  LabelControl.Left := 0;
  LabelControl.Top := ScaleY(ATop);
  LabelControl.Width := AParent.ClientWidth;
  LabelControl.Height := ScaleY(AHeight);
  LabelControl.Alignment := taCenter;
  LabelControl.AutoSize := False;
  LabelControl.WordWrap := True;
  LabelControl.Font.Name := 'Segoe UI';
  LabelControl.Font.Size := AFontSize;
  if ABold then
    LabelControl.Font.Style := [fsBold]
  else
    LabelControl.Font.Style := [];
end;

procedure ConfigureLogo(Logo: TBitmapImage; AParent: TWinControl; ATop: Integer);
begin
  Logo.Parent := AParent;
  Logo.Width := ScaleX(44);
  Logo.Height := ScaleY(44);
  Logo.Left := (AParent.ClientWidth - Logo.Width) div 2;
  Logo.Top := ScaleY(ATop);
  Logo.Stretch := False;
  Logo.BackColor := clNone;
  Logo.PngImage.LoadFromFile(ExpandConstant('{tmp}\FlowIME.Mark.44.png'));
end;

procedure ConfigureLeftLabel(LabelControl: TNewStaticText; AParent: TWinControl;
  const Caption: String; ALeft, ATop, AWidth, AHeight, AFontSize: Integer;
  ABold: Boolean);
begin
  LabelControl.Parent := AParent;
  LabelControl.Caption := Caption;
  LabelControl.Left := ScaleX(ALeft);
  LabelControl.Top := ScaleY(ATop);
  LabelControl.Width := ScaleX(AWidth);
  LabelControl.Height := ScaleY(AHeight);
  LabelControl.Alignment := taLeftJustify;
  LabelControl.AutoSize := False;
  LabelControl.WordWrap := True;
  LabelControl.Font.Name := 'Segoe UI';
  LabelControl.Font.Size := AFontSize;
  if ABold then
    LabelControl.Font.Style := [fsBold]
  else
    LabelControl.Font.Style := [];
end;

procedure InitializeProductHeader;
begin
  SettingsLogo := TBitmapImage.Create(WizardForm);
  SettingsLogo.Parent := WizardForm.MainPanel;
  SettingsLogo.Width := ScaleX(44);
  SettingsLogo.Height := ScaleY(44);
  SettingsLogo.Left := ScaleX(58);
  SettingsLogo.Top := ScaleY(20);
  SettingsLogo.Stretch := False;
  SettingsLogo.BackColor := clNone;
  SettingsLogo.PngImage.LoadFromFile(ExpandConstant('{tmp}\FlowIME.Mark.44.png'));

  SettingsTitle := TNewStaticText.Create(WizardForm);
  ConfigureLeftLabel(SettingsTitle, WizardForm.MainPanel,
    '安装 FlowIME', 122, 14, 430, 30, 18, True);

  SettingsSubtitle := TNewStaticText.Create(WizardForm);
  ConfigureLeftLabel(SettingsSubtitle, WizardForm.MainPanel,
    '自动切换到正确的输入法', 122, 46, 430, 24, 9, False);
  SettingsSubtitle.Font.Color := $00666666;
end;

function StopRunningFlowIME(const InstallPath: String): Boolean;
var
  ResultCode: Integer;
  InstalledExecutable: String;
  PowerShellScript: String;
  PowerShellArguments: String;
begin
  Result := True;
  InstalledExecutable := AddBackslash(InstallPath) + '{#AppExeName}';
  if FileExists(InstalledExecutable) then
  begin
    { Newer versions shut down through the single-instance IPC channel. }
    Exec(InstalledExecutable, '--shutdown', InstallPath,
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(250);
  end;

  { Wait for the exact installed executable. Legacy builds cannot consume the
    shutdown signal, so force-stop only that absolute path after ten seconds.
    Never terminate every process that merely shares FlowIME.App.exe's name. }
  PowerShellScript :=
    '& { param([string]$target) ' +
    '$deadline=[DateTime]::UtcNow.AddSeconds(10); ' +
    'do { ' +
      '$p=Get-Process -Name ''FlowIME.App'' -ErrorAction SilentlyContinue | ' +
        'Where-Object { $_.Path -eq $target }; ' +
      'if (-not $p) { exit 0 }; ' +
      'Start-Sleep -Milliseconds 250 ' +
    '} while ([DateTime]::UtcNow -lt $deadline); ' +
    '$p | Stop-Process -Force -PassThru | Wait-Process -ErrorAction SilentlyContinue; ' +
    '$left=Get-Process -Name ''FlowIME.App'' -ErrorAction SilentlyContinue | ' +
      'Where-Object { $_.Path -eq $target }; ' +
    'if ($left) { exit 1 } else { exit 0 } }';
  PowerShellArguments := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ' +
    AddQuotes(PowerShellScript) + ' ' + AddQuotes(InstalledExecutable);

  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    PowerShellArguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := False
  else
    Result := ResultCode = 0;
end;

function IsExistingInstall: Boolean;
begin
  Result := RegKeyExists(HKCU, UninstallKeyPath);
end;

function ShouldEnableStartup: Boolean;
begin
  Result := Assigned(StartupCheckBox) and StartupCheckBox.Checked;
end;

function IsUnsafeInstallPath(const Candidate: String): Boolean;
var
  DataPath: String;
begin
  DataPath := AddBackslash(ExpandConstant('{localappdata}')) + DataDirectoryName;
  Result := CompareText(RemoveBackslashUnlessRoot(Candidate),
    RemoveBackslashUnlessRoot(DataPath)) = 0;
end;

procedure BrowseButtonClick(Sender: TObject);
var
  SelectedDirectory: String;
begin
  SelectedDirectory := FolderEdit.Text;
  if BrowseForFolder('选择 FlowIME 安装位置', SelectedDirectory, True) then
    FolderEdit.Text := SelectedDirectory;
end;

procedure InitializeSettingsPage;
var
  InitialStartupEnabled: Boolean;
begin
  SettingsPage := CreateCustomPage(wpWelcome, '', '');
  SettingsPage.Surface.Color := clWhite;

  SettingsSectionTitle := TNewStaticText.Create(SettingsPage);
  ConfigureLeftLabel(SettingsSectionTitle, SettingsPage.Surface,
    '安装设置', 54, 28, 420, 26, 12, True);

  SettingsDescription := TNewStaticText.Create(SettingsPage);
  ConfigureLeftLabel(SettingsDescription, SettingsPage.Surface,
    '确认安装位置和启动偏好，然后即可开始安装。', 54, 58, 470, 24, 9, False);
  SettingsDescription.Font.Color := $00666666;

  PathLabel := TNewStaticText.Create(SettingsPage);
  PathLabel.Parent := SettingsPage.Surface;
  PathLabel.Caption := '安装位置';
  PathLabel.Left := ScaleX(54);
  PathLabel.Top := ScaleY(103);
  PathLabel.Width := ScaleX(380);
  PathLabel.Height := ScaleY(22);
  PathLabel.Font.Name := 'Segoe UI';
  PathLabel.Font.Size := 9;
  PathLabel.Font.Style := [fsBold];

  FolderEdit := TNewEdit.Create(SettingsPage);
  FolderEdit.Parent := SettingsPage.Surface;
  FolderEdit.Left := ScaleX(54);
  FolderEdit.Top := ScaleY(128);
  FolderEdit.Width := SettingsPage.SurfaceWidth - ScaleX(54 + 94 + 12);
  FolderEdit.Height := ScaleY(34);
  FolderEdit.Text := WizardForm.DirEdit.Text;
  FolderEdit.TabOrder := 0;
  PathLabel.FocusControl := FolderEdit;

  BrowseButton := TNewButton.Create(SettingsPage);
  BrowseButton.Parent := SettingsPage.Surface;
  BrowseButton.Caption := '浏览…';
  BrowseButton.Width := ScaleX(82);
  BrowseButton.Height := FolderEdit.Height;
  BrowseButton.Left := FolderEdit.Left + FolderEdit.Width + ScaleX(12);
  BrowseButton.Top := FolderEdit.Top;
  BrowseButton.OnClick := @BrowseButtonClick;
  BrowseButton.TabOrder := 1;

  StartupCheckBox := TNewCheckBox.Create(SettingsPage);
  StartupCheckBox.Parent := SettingsPage.Surface;
  StartupCheckBox.Caption := '登录 Windows 后自动启动 FlowIME';
  StartupCheckBox.Left := ScaleX(54);
  StartupCheckBox.Top := ScaleY(188);
  StartupCheckBox.Width := SettingsPage.SurfaceWidth - ScaleX(108);
  StartupCheckBox.Height := ScaleY(28);
  StartupCheckBox.TabOrder := 2;

  if IsExistingInstall then
    InitialStartupEnabled := RegValueExists(HKCU, RunKeyPath, 'FlowIME')
  else
    InitialStartupEnabled := True;
  StartupCheckBox.Checked := InitialStartupEnabled;

  InstallScopeLabel := TNewStaticText.Create(SettingsPage);
  ConfigureLeftLabel(InstallScopeLabel, SettingsPage.Surface,
    '仅为当前 Windows 账户安装，无需管理员权限', 54, 236, 470, 22, 9, False);
  InstallScopeLabel.Font.Color := $00666666;

  DataRetentionLabel := TNewStaticText.Create(SettingsPage);
  ConfigureLeftLabel(DataRetentionLabel, SettingsPage.Surface,
    '卸载应用时会保留个人设置与规则', 54, 260, 470, 22, 9, False);
  DataRetentionLabel.Font.Color := $00666666;

  VersionLabel := TNewStaticText.Create(SettingsPage);
  ConfigureLeftLabel(VersionLabel, SettingsPage.Surface,
    '版本 {#AppVersion}  ·  Windows 11 x64', 54, 282, 470, 20, 8, False);
  VersionLabel.Font.Color := $00666666;
end;

procedure InitializeInstallingPage;
begin
  InstallingTitle := TNewStaticText.Create(WizardForm.InstallingPage);
  ConfigureLeftLabel(InstallingTitle, WizardForm.InstallingPage,
    '正在安装所需组件', 54, 58, 470, 30, 14, True);

  InstallingHint := TNewStaticText.Create(WizardForm.InstallingPage);
  ConfigureLeftLabel(InstallingHint, WizardForm.InstallingPage,
    '请稍候，不要关闭安装程序。', 54, 92, 470, 24, 9, False);
  InstallingHint.Font.Color := $00666666;

  WizardForm.StatusLabel.Parent := WizardForm.InstallingPage;
  WizardForm.StatusLabel.Left := ScaleX(54);
  WizardForm.StatusLabel.Top := ScaleY(154);
  WizardForm.StatusLabel.Width := WizardForm.InstallingPage.ClientWidth - ScaleX(108);
  WizardForm.StatusLabel.Height := ScaleY(24);
  WizardForm.StatusLabel.Alignment := taLeftJustify;
  WizardForm.StatusLabel.Caption := '正在配置 FlowIME...';

  WizardForm.ProgressGauge.Parent := WizardForm.InstallingPage;
  WizardForm.ProgressGauge.Left := ScaleX(54);
  WizardForm.ProgressGauge.Top := ScaleY(184);
  WizardForm.ProgressGauge.Width := WizardForm.InstallingPage.ClientWidth - ScaleX(108);
  WizardForm.ProgressGauge.Height := ScaleY(10);

  WizardForm.FilenameLabel.Visible := False;
end;

procedure InitializeFinishedPage;
begin
  WizardForm.WizardBitmapImage.Visible := False;
  WizardForm.WizardBitmapImage2.Visible := False;
  WizardForm.FinishedPage.Left := 0;
  WizardForm.FinishedPage.Width := WizardForm.InnerNotebook.ClientWidth;

  WizardForm.FinishedHeadingLabel.Visible := False;
  WizardForm.FinishedLabel.Visible := False;
  WizardForm.RunList.Visible := False;
  WizardForm.FinishedPage.Color := clWhite;

  FinishedLogo := TBitmapImage.Create(WizardForm.FinishedPage);
  FinishedLogo.Parent := WizardForm.FinishedPage;
  FinishedLogo.Width := ScaleX(44);
  FinishedLogo.Height := ScaleY(44);
  FinishedLogo.Left := ScaleX(58);
  FinishedLogo.Top := ScaleY(32);
  FinishedLogo.Stretch := False;
  FinishedLogo.BackColor := clNone;
  FinishedLogo.PngImage.LoadFromFile(ExpandConstant('{tmp}\FlowIME.Mark.44.png'));

  FinishedBrandTitle := TNewStaticText.Create(WizardForm.FinishedPage);
  ConfigureLeftLabel(FinishedBrandTitle, WizardForm.FinishedPage,
    'FlowIME', 122, 26, 410, 30, 18, True);

  FinishedBrandSubtitle := TNewStaticText.Create(WizardForm.FinishedPage);
  ConfigureLeftLabel(FinishedBrandSubtitle, WizardForm.FinishedPage,
    '自动切换到正确的输入法', 122, 58, 410, 24, 9, False);
  FinishedBrandSubtitle.Font.Color := $00666666;

  SuccessMark := TNewStaticText.Create(WizardForm.FinishedPage);
  ConfigureLeftLabel(SuccessMark, WizardForm.FinishedPage,
    '✓', 54, 142, 42, 48, 25, True);
  SuccessMark.Font.Color := $005C8B2F;

  FinishedTitle := TNewStaticText.Create(WizardForm.FinishedPage);
  ConfigureLeftLabel(FinishedTitle, WizardForm.FinishedPage,
    '安装完成', 108, 138, 410, 34, 17, True);

  FinishedSubtitle := TNewStaticText.Create(WizardForm.FinishedPage);
  ConfigureLeftLabel(FinishedSubtitle, WizardForm.FinishedPage,
    '现在可以启动应用，FlowIME 会在需要时自动切换输入法。',
    108, 176, 430, 44, 9, False);
  FinishedSubtitle.Font.Color := $00666666;
end;

procedure ConfigureWizardChrome;
begin
  WizardForm.Caption := 'FlowIME 安装程序';
  WizardForm.PageNameLabel.Visible := False;
  WizardForm.PageDescriptionLabel.Visible := False;
  WizardForm.WizardSmallBitmapImage.Visible := False;
  WizardForm.BackButton.Visible := False;

  WizardForm.NextButton.Width := ScaleX(126);
  WizardForm.NextButton.Height := ScaleY(34);
  WizardForm.NextButton.Left := WizardForm.ClientWidth - WizardForm.NextButton.Width - ScaleX(24);
  WizardForm.CancelButton.Width := ScaleX(92);
  WizardForm.CancelButton.Height := WizardForm.NextButton.Height;
  WizardForm.CancelButton.Left := WizardForm.NextButton.Left - WizardForm.CancelButton.Width - ScaleX(10);
end;

procedure InitializeWizard;
begin
  ExtractTemporaryFile('FlowIME.Mark.44.png');
  LaunchOnFinish := True;
  ConfigureWizardChrome;
  InitializeProductHeader;
  InitializeSettingsPage;
  InitializeInstallingPage;
  InitializeFinishedPage;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  InstallPath: String;
  ResultCode: Integer;
begin
  Result := True;

  if CurPageID = SettingsPage.ID then
  begin
    InstallPath := Trim(FolderEdit.Text);
    if InstallPath = '' then
    begin
      MsgBox('请选择 FlowIME 的安装位置。', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if IsUnsafeInstallPath(InstallPath) then
    begin
      MsgBox('程序目录不能与 %LOCALAPPDATA%\FlowIME 用户数据目录相同。' + #13#10 +
        '请选择其他位置，避免升级时覆盖配置。', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    WizardForm.DirEdit.Text := InstallPath;
    if not StopRunningFlowIME(InstallPath) then
    begin
      MsgBox('无法安全关闭此安装目录中的 FlowIME。' + #13#10 +
        '请手动退出 FlowIME 后重试。', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end
  else if CurPageID = wpFinished then
  begin
    if LaunchOnFinish and (not WizardSilent) then
    begin
      if not ExecAsOriginalUser(ExpandConstant('{app}\{#AppExeName}'), '',
        ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode) then
      begin
        MsgBox('FlowIME 已安装，但未能自动启动。' + #13#10 +
          '你可以从开始菜单手动启动。', mbError, MB_OK);
      end;
    end;
  end;
end;

procedure FinishedCloseButtonClick(Sender: TObject);
begin
  LaunchOnFinish := False;
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  ConfigureWizardChrome;

  if CurPageID = SettingsPage.ID then
  begin
    SettingsTitle.Caption := '安装 FlowIME';
    SettingsSubtitle.Caption := '自动切换到正确的输入法';
    WizardForm.NextButton.Caption := '安装 FlowIME';
    WizardForm.CancelButton.Caption := '取消';
    WizardForm.NextButton.Visible := True;
    WizardForm.CancelButton.Visible := True;
    WizardForm.ActiveControl := WizardForm.NextButton;
  end
  else if CurPageID = wpInstalling then
  begin
    SettingsTitle.Caption := '正在安装 FlowIME';
    SettingsSubtitle.Caption := '正在复制文件并配置应用';
    WizardForm.NextButton.Visible := False;
    WizardForm.CancelButton.Caption := '取消';
  end
  else if CurPageID = wpFinished then
  begin
    SettingsTitle.Caption := '安装完成';
    SettingsSubtitle.Caption := 'FlowIME 已准备就绪';
    WizardForm.NextButton.Caption := '启动 FlowIME';
    WizardForm.CancelButton.Caption := '关闭';
    WizardForm.CancelButton.OnClick := @FinishedCloseButtonClick;
    WizardForm.NextButton.Visible := True;
    WizardForm.CancelButton.Visible := True;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and (not ShouldEnableStartup) then
    RegDeleteValue(HKCU, RunKeyPath, 'FlowIME');
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if Assigned(InstallingHint) then
    InstallingHint.Caption := '请稍候，不要关闭安装程序';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if not StopRunningFlowIME(ExpandConstant('{app}')) then
      RaiseException('无法安全关闭 FlowIME，卸载已停止。');

    if RegQueryStringValue(HKCU, RunKeyPath, 'FlowIME', StartupCommand) and
       (Pos(Lowercase(ExpandConstant('{app}\')), Lowercase(StartupCommand)) > 0) then
      RegDeleteValue(HKCU, RunKeyPath, 'FlowIME');
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    { Remove the now-empty published directory tree without deleting unknown
      files a user may have placed in a custom installation directory. }
    DelTree(ExpandConstant('{app}'), True, False, True);
  end;
end;
