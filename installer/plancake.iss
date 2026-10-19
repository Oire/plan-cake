; PlanCake Inno Setup installer script
; Copyright © 2026 Oire Software SARL.
;
; Build it with Build-Installer.ps1, which compiles the translations, publishes the app and
; then runs ISCC on this file. Saved as UTF-8 with a BOM: without one ISCC reads the script
; in the ANSI code page.

#define MyAppName "PlanCake"
#define MyAppPublisher "Oire Software SARL"
#define MyAppCompany "Oire"
#define MyAppFolderName "PlanCake"
#define MyAppURL "https://plan-cake.oire.dev"
#define MyAppSupportURL "https://github.com/Oire/plan-cake/issues"
#define MyAppExeName "plancake.exe"

; Where the machine-wide environment, the PATH among it, lives.
#define EnvironmentKey "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"

; PlanCake's value name under Software\RegisteredApplications. Must equal
; DefaultApps.RegisteredName, which Settings > Advanced puts in the Default apps link
; (InstallerTests checks).
#define RegisteredAppName "PlanCake"

; The file type (ProgID) PlanCake registers for .md and .markdown files.
#define AppProgId "PlanCake.Markdown"

; Where PlanCake lists its capabilities for Default apps.
#define CapabilitiesKey "Software\Oire\PlanCake\Capabilities"

; The publish folder Build-Installer.ps1 writes to (relative to this script).
#define SourcePath "..\src\PlanCake\bin\x64\Release\publish"

; The version comes from the published executable, which GitVersion stamped.
#define MyAppVersion GetVersionNumbersString(SourcePath + "\" + MyAppExeName)

[Setup]
; The AppId identifies PlanCake to Windows and to winget across every version: never change it.
AppId={{71654D7C-5454-4DAB-B1DC-5874D6358D83}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppSupportURL}
AppUpdatesURL={#MyAppURL}
AppCopyright=Copyright © 2026 {#MyAppPublisher}.
VersionInfoVersion={#MyAppVersion}

; Installation directory
DefaultDirName={autopf}\{#MyAppCompany}\{#MyAppName}
DefaultGroupName={#MyAppCompany}\{#MyAppName}
AllowNoIcons=yes

; Output configuration
OutputDir=Output
OutputBaseFilename=plancake-v{#MyAppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; The wizard's own icon. Shortcuts and Apps & features take theirs from the exe, which
; carries the same icon (ApplicationIcon in PlanCake.csproj), so the .ico is not shipped.
SetupIconFile=..\src\PlanCake\PlanCake.ico

; Uninstall configuration
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExeName}

; System requirements
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; License file
LicenseFile=..\LICENSE

; Privileges: the install folder is under Program Files and the PATH entry is machine-wide.
PrivilegesRequired=admin
DisableProgramGroupPage=yes
; The Ready to Install page stays: it lists the .NET and WebView2 runtimes Setup is about to
; download (CodeDependencies.iss, UpdateReadyMemo), and its button is the one that says Install.

; {app} goes on the machine PATH (see [Registry]); tell running programs to reload it.
ChangesEnvironment=yes
; PlanCake registers itself for .md and .markdown (see [Registry]); tell Explorer to reload
; the file associations.
ChangesAssociations=yes

; Language options
ShowLanguageDialog=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl,Languages\Custom.en.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl,Languages\Custom.ru.isl"
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl,Languages\Custom.uk.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl,Languages\Custom.fr.isl"
Name: "he"; MessagesFile: "compiler:Languages\Hebrew.isl,Languages\Custom.he.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl,Languages\Custom.de.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The same set Build-Installer.ps1 puts in the portable zip ($ShippedItems there). The single-file
; publish leaves these beside the exe: WebView2Loader.dll (the native loader, which a single-file
; bundle cannot hold), the page WebView2 shows, the user manual, the compiled catalogs, and
; PlanCake's license and the third-party notices (Help > About > Licenses opens them).
Source: "{#SourcePath}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\web\*"; DestDir: "{app}\web"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourcePath}\help\*"; DestDir: "{app}\help"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourcePath}\locale\*.mo"; DestDir: "{app}\locale"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{cm:AppDescription}"
; The manual in the language Setup ran in: the [Languages] names are the help\<code> folders.
Name: "{group}\{cm:ManualShortcut}"; Filename: "{app}\help\{language}\manual.html"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{cm:AppDescription}"; Tasks: desktopicon

[Registry]
; Put {app} on the machine PATH so `plancake` works in any new console. Appended to the
; existing value ({olddata}) and skipped when it is already there. No uninsdeletevalue flag:
; that would delete the whole Path value. RemoveAppFromPath takes the entry out on uninstall.
Root: HKLM; Subkey: "{#EnvironmentKey}"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Check: NeedsAddPath(ExpandConstant('{app}'))

; PlanCake as one of the programs that open .md and .markdown files: a file type, an entry in
; each extension's OpenWithProgids, an Applications entry, and capabilities listed under
; RegisteredApplications, so PlanCake shows in Open with and in Default apps.
; - The extensions' default value is never written: it would take Markdown files away from the
;   program the user or Windows picked.
; - The user makes PlanCake the default in Default apps, which Settings > Advanced opens.
; - No verb but open: File > Open in editor looks for an edit verb, and must not find PlanCake.
; - The parent keys come first because Inno Setup uninstalls in reverse order, so an emptied key
;   is checked after its children are gone.
Root: HKLM; Subkey: "Software\Classes\{#AppProgId}"; ValueType: string; ValueName: ""; ValueData: "{cm:MarkdownFileType}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\{#AppProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Classes\{#AppProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKLM; Subkey: "Software\Classes\.md"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "{#AppProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\.markdown"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "{#AppProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKLM; Subkey: "Software\Oire"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\Oire\PlanCake"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "{#CapabilitiesKey}"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "{#CapabilitiesKey}"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "{cm:AppDescription}"
Root: HKLM; Subkey: "{#CapabilitiesKey}"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "{#CapabilitiesKey}\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "{#AppProgId}"
Root: HKLM; Subkey: "{#CapabilitiesKey}\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "{#AppProgId}"
Root: HKLM; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#RegisteredAppName}"; ValueData: "{#CapabilitiesKey}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

#include "CodeDependencies.iss"

[Code]
const
  EnvironmentKey = '{#EnvironmentKey}';

// True when two PATH entries name the same folder: case-insensitive, trailing backslash ignored.
function IsSamePathEntry(const Entry, Dir: String): Boolean;
begin
  Result := CompareText(RemoveBackslashUnlessRoot(Trim(Entry)), RemoveBackslashUnlessRoot(Trim(Dir))) = 0;
end;

// Splits off the first entry of a semicolon-separated list; Rest keeps what follows the semicolon.
function NextPathEntry(var Rest: String): String;
var
  P: Integer;
begin
  P := Pos(';', Rest);
  if P = 0 then begin
    Result := Rest;
    Rest := '';
  end else begin
    Result := Copy(Rest, 1, P - 1);
    Delete(Rest, 1, P);
  end;
end;

// [Registry] Check: add {app} to the machine PATH only when it is not there yet.
function NeedsAddPath(const Dir: String): Boolean;
var
  Paths, Rest: String;
begin
  if not RegQueryStringValue(HKLM, EnvironmentKey, 'Path', Paths) then begin
    Result := True;
    Exit;
  end;

  Result := True;
  Rest := Paths;
  while Rest <> '' do begin
    if IsSamePathEntry(NextPathEntry(Rest), Dir) then begin
      Result := False;
      Exit;
    end;
  end;
end;

// Takes every entry naming Dir out of the machine PATH and leaves the others as they were.
procedure RemoveAppFromPath(const Dir: String);
var
  Paths, Rest, Entry, NewPaths: String;
  Removed: Boolean;
begin
  if not RegQueryStringValue(HKLM, EnvironmentKey, 'Path', Paths) then
    Exit;

  NewPaths := '';
  Removed := False;
  Rest := Paths;
  while Rest <> '' do begin
    Entry := NextPathEntry(Rest);
    if IsSamePathEntry(Entry, Dir) then
      Removed := True
    else if NewPaths = '' then
      NewPaths := Entry
    else
      NewPaths := NewPaths + ';' + Entry;
  end;

  if Removed then
    RegWriteExpandStringValue(HKLM, EnvironmentKey, 'Path', NewPaths);
end;

function InitializeSetup: Boolean;
begin
  // Force x64 dependencies since PlanCake is 64-bit only
  Dependency_ForceX86 := False;

  // The .NET 10 Desktop Runtime, and the WebView2 Runtime that shows the documents
  Dependency_AddDotNet100Desktop;
  Dependency_AddWebView2;

  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserDataPath: String;
  LocalDataPath: String;
begin
  if CurUninstallStep = usUninstall then
    RemoveAppFromPath(ExpandConstant('{app}'));

  if CurUninstallStep = usPostUninstall then begin
    // Settings and logs (App.DataFolder, roaming) and the WebView2 working files
    // (App.WebView2DataFolder, local). A silent uninstall (winget) keeps them:
    // SuppressibleMsgBox answers No without asking.
    // {userappdata} and {localappdata} are the folders of the account the uninstaller runs
    // as. When a standard user uninstalls with an administrator's password, that is the
    // administrator: the question is not asked and the user's own folders stay. Inno Setup
    // offers no way to reach the original user here (ExecAsOriginalUser does not work at
    // uninstall time), so the manuals tell such a user to delete the folders by hand.
    UserDataPath := ExpandConstant('{userappdata}\{#MyAppCompany}\{#MyAppFolderName}');
    LocalDataPath := ExpandConstant('{localappdata}\{#MyAppCompany}\{#MyAppFolderName}');
    if DirExists(UserDataPath) or DirExists(LocalDataPath) then begin
      if SuppressibleMsgBox(CustomMessage('RemoveUserData'),
                            mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then begin
        DelTree(UserDataPath, True, True, True);
        DelTree(LocalDataPath, True, True, True);
        // Removes the Oire folders only when no other Oire application still uses them
        RemoveDir(ExpandConstant('{userappdata}\{#MyAppCompany}'));
        RemoveDir(ExpandConstant('{localappdata}\{#MyAppCompany}'));
      end;
    end;
  end;
end;
