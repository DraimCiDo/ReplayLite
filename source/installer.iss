; Compile with the pinned Inno Setup 7.1.0 compiler. Payload contains plain files,
; not a second executable or a ZIP embedded inside a managed code section.
#ifndef PackageRoot
  #error PackageRoot is required
#endif
#ifndef OutputRoot
  #error OutputRoot is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifdef TestBuild
  #define RegistryName "ReplayLite-DeploymentTest"
#else
  #define RegistryName "ReplayLite"
#endif

[Setup]
AppId={#RegistryName}
AppName=ReplayLite
AppVersion={#AppVersion}
AppPublisher=DraimCiDo
AppPublisherURL=https://github.com/DraimCiDo/ReplayLite
AppSupportURL=https://github.com/DraimCiDo/ReplayLite/issues
AppUpdatesURL=https://github.com/DraimCiDo/ReplayLite/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=ReplayLite installer
DefaultDirName={localappdata}\Programs\ReplayLite
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir={#OutputRoot}
OutputBaseFilename=ReplayLite-Setup
SetupIconFile={#PackageRoot}\assets\ReplayLite.ico
WizardStyle=modern dark
Compression=zip
SolidCompression=no
UsePreviousAppDir=no
UsePreviousTasks=no
CloseApplications=no
RestartApplications=no
; Keep the existing manifest-based uninstaller: the in-app updater maintains its
; list, including files added by newer versions. Existing installations keep it.
Uninstallable=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Check: not IsPortable

[Files]
Source: "{#PackageRoot}\*"; DestDir: "{app}"; Excludes: "portable.flag,installed.flag"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageRoot}\portable.flag"; DestDir: "{app}"; Flags: ignoreversion; Check: IsPortable
Source: "{#PackageRoot}\installed.flag"; DestDir: "{app}"; Flags: ignoreversion; Check: not IsPortable

[Icons]
Name: "{userprograms}\{#RegistryName}"; Filename: "{app}\ReplayLite.exe"; WorkingDir: "{app}"; Check: not IsPortable
Name: "{userdesktop}\{#RegistryName}"; Filename: "{app}\ReplayLite.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Check: not IsPortable

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RegistryName}"; ValueType: string; ValueName: "DisplayName"; ValueData: "ReplayLite"; Check: not IsPortable
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RegistryName}"; ValueType: string; ValueName: "DisplayVersion"; ValueData: "{#AppVersion}"; Check: not IsPortable
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RegistryName}"; ValueType: string; ValueName: "Publisher"; ValueData: "DraimCiDo"; Check: not IsPortable
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RegistryName}"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Check: not IsPortable
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RegistryName}"; ValueType: string; ValueName: "DisplayIcon"; ValueData: "{app}\ReplayLite.exe"; Check: not IsPortable
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RegistryName}"; ValueType: string; ValueName: "UninstallString"; ValueData: """{app}\Uninstall.exe"""; Check: not IsPortable
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RegistryName}"; ValueType: dword; ValueName: "NoModify"; ValueData: "1"; Check: not IsPortable
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RegistryName}"; ValueType: dword; ValueName: "NoRepair"; ValueData: "1"; Check: not IsPortable

[Run]
Filename: "{app}\ReplayLite.exe"; Description: "{cm:LaunchProgram,ReplayLite}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ModePage: TInputOptionWizardPage;
  PreviousMode: Integer;

function IsPortable: Boolean;
begin
  Result := ModePage.SelectedValueIndex = 1;
end;

procedure InitializeWizard;
begin
  ModePage := CreateInputOptionPage(wpWelcome, 'ReplayLite',
    'Выберите способ установки', 'Настройки и записи сохраняются при обновлении.', True, False);
  ModePage.Add('Установить для текущего пользователя');
  ModePage.Add('Portable — всё в выбранной папке, без ярлыков и регистрации');
  if ExpandConstant('{param:MODE|installed}') = 'portable' then
    ModePage.SelectedValueIndex := 1
  else
    ModePage.SelectedValueIndex := 0;
  PreviousMode := -1;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  if (CurPageID = ModePage.ID) and (PreviousMode <> ModePage.SelectedValueIndex) then begin
    if ExpandConstant('{param:DIR|}') = '' then begin
      if IsPortable then
        WizardForm.DirEdit.Text := ExpandConstant('{userdesktop}\ReplayLite')
      else
        WizardForm.DirEdit.Text := ExpandConstant('{localappdata}\Programs\ReplayLite');
    end;
    PreviousMode := ModePage.SelectedValueIndex;
  end;
  Result := True;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpSelectTasks) and IsPortable;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir, Parent: String;
  FindRec: TFindRec;
begin
  Result := '';
  Dir := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  if Length(Dir) <= 3 then begin
    Result := 'Выберите отдельную папку ReplayLite.';
    Exit;
  end;
  Parent := Dir;
  while Length(Parent) > 3 do begin
    if DirExists(Parent) and FindFirst(Parent, FindRec) then begin
      try
        if (FindRec.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0 then begin
          Result := 'Установка в ссылку не поддерживается.';
          Exit;
        end;
      finally
        FindClose(FindRec);
      end;
    end;
    Parent := ExtractFileDir(Parent);
  end;
  if FindFirst(AddBackslash(Dir) + '*', FindRec) then begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then begin
          Result := 'Для первой установки выберите пустую папку. Обновление доступно из приложения.';
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;
