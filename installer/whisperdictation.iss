; Copyright (c) 2026 Rausch IT
; SPDX-License-Identifier: MIT

; ===========================================================================
; WhisperDictation Installer
; Requires: Inno Setup 6.3+ (resolved by build-installer.ps1 from the Tools.InnoSetup NuGet package)
; Build:    Run build-installer.ps1 (publishes the app, then compiles this file)
;
; The model files are downloaded by Setup itself while it installs ([Files] entries
; with the "external download" flags, Source = URL) from the Rausch IT web server and
; verified by SHA-256. They are far too large for the repository (3.5 GB in total) and
; GitHub rejects release assets above 2 GB. Existing model files with the expected size
; are kept and skipped.
;
; Free disk space is checked in [Code] (CheckDiskSpace) once the models are chosen,
; i.e. when leaving the task page, and again in PrepareToInstall for silent installs.
; The built-in check on the directory page runs before the task selection and
; therefore cannot know which models will be downloaded.
; ===========================================================================

#define AppName      "WhisperDictation"
#define AppPublisher "Rausch IT"
#define AppExeName   "WhisperDictation.App.exe"
; Whisper worker process started by the app; it ends when the app does.
#define WorkerExeName "WhisperDictation.Core.exe"
#ifndef PublishDir
  #define PublishDir "..\build\publish"
#endif
#define IconFile     "..\WhisperDictation.App\Assets\app-mic.ico"

#define FileHandle   FileOpen(AddBackslash(SourcePath) + "..\VERSION")
#define RawVersion   Trim(FileRead(FileHandle))
#expr                FileClose(FileHandle)
#define AppVersion   RawVersion

; Numeric version for Windows file version info (strip prerelease suffix)
#if Pos("-", RawVersion) > 0
  #define NumericVersion Copy(RawVersion, 1, Pos("-", RawVersion) - 1)
#else
  #define NumericVersion RawVersion
#endif

; Size of the application files in bytes, passed by build-installer.ps1.
#ifndef AppSize
  #define AppSize "300000000"
#endif

; ---- Model files (sizes in bytes must match the files offered for download) --
; Hosted by Rausch IT (HuggingFace was too slow from Europe). The SHA-256 values match
; SHA256SUMS on the server; Setup verifies every download against them.
#define ModelBaseUrl "https://www.rauschit.de/downloads/whisper-dictation/models/"
#define VadName    "silero_vad.onnx"
#define VadSize    "2243022"
#define VadHash    "a4a068cd6cf1ea8355b84327595838ca748ec29a25bc91fc82e6c299ccdc5808"
#define LargeName  "ggml-large-v3.bin"
#define LargeSize  "3095033483"
#define LargeHash  "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2"
#define SmallName  "ggml-small.bin"
#define SmallSize  "487601967"
#define SmallHash  "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"

; ===========================================================================
[Setup]
; Stable GUID — must never change between versions
AppId={{32232D0C-268E-4612-9376-AE8BC5AADD51}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}.0
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\WhisperDictation
DefaultGroupName={#AppName}
OutputDir=..\Releases\{#AppVersion}
OutputBaseFilename=WhisperDictation-{#AppVersion}-setup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-user install without admin rights: {autopf} resolves to %LOCALAPPDATA%\Programs,
; where the app can also write config.json. Only installing a missing .NET runtime
; needs elevation; that step asks for it on its own (see InstallRuntime).
PrivilegesRequired=lowest
WizardStyle=modern
MinVersion=10.0
CloseApplications=force
CloseApplicationsFilter={#AppExeName},{#WorkerExeName}
RestartApplications=no

; ===========================================================================
[Languages]
; English only, like the app (not localized). A single language also skips the language dialog.
Name: "english"; MessagesFile: "compiler:Default.isl"

; ===========================================================================
[Tasks]
Name: "autostart";   Description: "Start WhisperDictation with Windows"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}";               GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
; Both can be installed; the model and GPU/CPU are chosen in the app and can be switched there.
Name: "whisperlarge"; Description: "Whisper model large-v3 (2.9 GB) — best quality, recommended with a Vulkan-capable GPU"; GroupDescription: "Models to download (switchable in the app):"
Name: "whispersmall"; Description: "Whisper model small (466 MB) — faster, lower quality, suitable for CPU";               GroupDescription: "Models to download (switchable in the app):"

; ===========================================================================
[Files]
; The application itself (published without the models).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
; Licence texts: the third-party licences (MIT, BSD-3-Clause) require the notices to be shipped.
Source: "..\LICENSE";                DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

; Model files, downloaded by Setup. Silero VAD is required in every configuration.
; Check functions only accept String/Integer/Boolean parameters, so ModelMissing
; takes the file name and looks the size up in [Code] (sizes exceed 2 GB).
Source: "{#ModelBaseUrl}{#VadName}"; DestDir: "{app}\models"; DestName: "{#VadName}"; \
  Flags: external download ignoreversion; ExternalSize: {#VadSize}; Hash: "{#VadHash}"; \
  Check: ModelMissing('{#VadName}')
Source: "{#ModelBaseUrl}{#LargeName}"; DestDir: "{app}\models"; DestName: "{#LargeName}"; \
  Tasks: whisperlarge; Flags: external download ignoreversion; ExternalSize: {#LargeSize}; Hash: "{#LargeHash}"; \
  Check: ModelMissing('{#LargeName}')
Source: "{#ModelBaseUrl}{#SmallName}"; DestDir: "{app}\models"; DestName: "{#SmallName}"; \
  Tasks: whispersmall; Flags: external download ignoreversion; ExternalSize: {#SmallSize}; Hash: "{#SmallHash}"; \
  Check: ModelMissing('{#SmallName}')

; ===========================================================================
[Registry]
; Same value the app would write itself — keep both in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WhisperDictation"; \
  ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

; ===========================================================================
[Icons]
Name: "{group}\{#AppName}";                       Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";                 Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

; ===========================================================================
[Run]
Filename: "{app}\{#AppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

; ===========================================================================
[UninstallRun]
; The app has no window to close; stop it so its files can be removed.
; config.json and the downloaded models live in {app} and are removed with it.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExeName} /F"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#WorkerExeName} /F"; Flags: runhidden; RunOnceId: "StopWorker"

; ===========================================================================
[UninstallDelete]
; Model files that were already present are skipped during setup and therefore not
; registered for uninstall; remove the folder explicitly.
Type: filesandordirs; Name: "{app}\models"

; ===========================================================================
[Code]

const
  RUNTIME_URL    = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  RUNTIME_EXE    = 'windowsdesktop-runtime-10-x64.exe';
  RUNTIME_PAGE   = 'https://dotnet.microsoft.com/download/dotnet/10.0';

  // Space the .NET runtime needs on the drive of {tmp}: its installer (~60 MB) plus the installed runtime.
  RUNTIME_SPACE_MB = 500;
  // Safety margin so the target drive is not filled to the last byte.
  SPACE_MARGIN_MB  = 200;

// ---- .NET Desktop Runtime detection -----------------------------------------
// Any .NET 8 or newer is accepted; if none is found, the current LTS (.NET 10) is offered.

// True when a release version (no "-preview" etc.) with major version 8 or higher is
// registered. The app targets .NET 8 with RollForward=LatestMajor, so it runs on 8, 9,
// 10, ...; prereleases are not used by roll-forward.
function HasVersion8OrNewer(const RootKey: Integer; const Name: String): Boolean;
var
  ValueNames: TArrayOfString;
  I, Dot: Integer;
begin
  Result := False;
  if RegGetValueNames(RootKey,
       'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\' + Name,
       ValueNames) then
  begin
    for I := 0 to GetArrayLength(ValueNames) - 1 do
    begin
      Dot := Pos('.', ValueNames[I]);
      if (Dot > 1) and (Pos('-', ValueNames[I]) = 0) and
         (StrToIntDef(Copy(ValueNames[I], 1, Dot - 1), 0) >= 8) then
      begin
        Log('.NET Desktop Runtime found: ' + ValueNames[I]);
        Result := True;
        Exit;
      end;
    end;
  end;
end;

// The .NET installers register in the 32-bit registry view (WOW6432Node), which a 64-bit setup does not
// see under plain HKLM (verified on the target machine). Check both views.
function IsDesktopRuntimeInstalled(): Boolean;
begin
  Result := HasVersion8OrNewer(HKLM32, 'Microsoft.WindowsDesktop.App') or
            HasVersion8OrNewer(HKLM64, 'Microsoft.WindowsDesktop.App');
end;

function OnDownloadProgress(const Url, FileName: string;
  const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax <> 0 then
    Log(Format('Downloading %s: %d of %d bytes', [FileName, Progress, ProgressMax]))
  else
    Log(Format('Downloading %s: %d bytes', [FileName, Progress]));
  Result := True;
end;

// Downloads and runs the .NET Desktop Runtime installer. The runtime only installs
// machine-wide, so this one step is started elevated ("runas"); Windows shows a UAC
// prompt. Returns '' on success, otherwise an error message.
function InstallRuntime(): String;
var
  ErrorCode: Integer;
begin
  Result := '';
  Log('.NET 10 Desktop Runtime: downloading...');
  try
    DownloadTemporaryFile(RUNTIME_URL, RUNTIME_EXE, '', @OnDownloadProgress);
  except
    Result := 'Failed to download the .NET 10 Desktop Runtime.'#13#10
            + 'Please install it manually from ' + RUNTIME_PAGE + #13#10
            + 'and run this installer again.';
    Exit;
  end;

  if not ShellExec('runas', ExpandConstant('{tmp}\') + RUNTIME_EXE, '/install /passive /norestart', '',
                   SW_SHOW, ewWaitUntilTerminated, ErrorCode) then
  begin
    // 1223 = ERROR_CANCELLED: the user declined the UAC prompt.
    Log(Format('.NET 10 Desktop Runtime installer not started: %d %s', [ErrorCode, SysErrorMessage(ErrorCode)]));
    Result := 'The .NET 10 Desktop Runtime was not installed.'#13#10
            + 'Please install it manually from ' + RUNTIME_PAGE + #13#10
            + 'and run this installer again.';
    Exit;
  end;

  // ShellExec does not report the exit code, so check the result directly.
  if IsDesktopRuntimeInstalled() then
    Log('.NET 10 Desktop Runtime installed successfully.')
  else
    Result := 'The .NET 10 Desktop Runtime installation did not complete.'#13#10
            + 'Please install it manually from ' + RUNTIME_PAGE + #13#10
            + 'and run this installer again.';
end;

// ---- Model files ------------------------------------------------------------

// True when the file exists and has exactly the expected size.
// FileSize64 is used because plain FileSize() stops working above 2 GB and the
// large Whisper model is about 2.9 GB.
function FileHasSize(const Path: String; const ExpectedSize: Int64): Boolean;
var
  ActualSize: Int64;
begin
  Result := False;
  if not FileExists(Path) then
    Exit;
  if not FileSize64(Path, ActualSize) then
    Exit;
  if ActualSize = ExpectedSize then
    Result := True;
end;

// Sizes come from ISPP string defines: integer literals above 2 GB are not reliable here.
function ModelSize(const Name: String): Int64;
begin
  if Name = '{#VadName}' then
    Result := StrToInt64('{#VadSize}')
  else if Name = '{#LargeName}' then
    Result := StrToInt64('{#LargeSize}')
  else if Name = '{#SmallName}' then
    Result := StrToInt64('{#SmallSize}')
  else
    RaiseException('Unknown model file: ' + Name);
end;

// Check function for the model entries: True when the file still has to be downloaded.
// The parameter is deliberately not "const" and not Int64, otherwise the compiler
// reports "Invalid prototype".
function ModelMissing(Name: String): Boolean;
begin
  Result := not FileHasSize(ExpandConstant('{app}\models\') + Name, ModelSize(Name));
end;

// Bytes of the model files that will actually be downloaded with the current task selection.
function ModelBytesToDownload(): Int64;
begin
  Result := 0;
  if ModelMissing('{#VadName}') then
    Result := Result + ModelSize('{#VadName}');
  if WizardIsTaskSelected('whisperlarge') and ModelMissing('{#LargeName}') then
    Result := Result + ModelSize('{#LargeName}');
  if WizardIsTaskSelected('whispersmall') and ModelMissing('{#SmallName}') then
    Result := Result + ModelSize('{#SmallName}');
end;

// ---- Disk space ---------------------------------------------------------------

function BytesToMB(const Bytes: Int64): Int64;
begin
  Result := (Bytes + 1048575) div 1048576;
end;

// Returns '' when Drive has at least Needed bytes free, otherwise a message for the user.
function CheckDrive(const Drive: String; const Needed: Int64): String;
var
  FreeBytes, TotalBytes: Int64;
begin
  Result := '';
  if not GetSpaceOnDisk64(AddBackslash(Drive), FreeBytes, TotalBytes) then
  begin
    // Should not block setup, e.g. on network shares that do not report free space.
    Log('Disk space: could not determine free space on ' + Drive + ', skipping check.');
    Exit;
  end;

  Log(Format('Disk space on %s: %d MB needed, %d MB free.', [Drive, BytesToMB(Needed), BytesToMB(FreeBytes)]));
  if FreeBytes < Needed then
    Result := Format('Not enough free disk space on drive %s.'#13#10#13#10
                   + 'Required:  %d MB'#13#10
                   + 'Available: %d MB'#13#10#13#10
                   + 'Free up disk space, choose another installation folder, or deselect a model.', [
                   Drive, BytesToMB(Needed), BytesToMB(FreeBytes)]);
end;

// Space needed on the target drive (application + selected models) and, if the .NET
// runtime still has to be installed, on the drive of {tmp} (0 when it is the same drive).
procedure GetSpaceNeeds(var AppDrive, TmpDrive: String; var AppNeeded, TmpNeeded: Int64);
begin
  AppDrive := ExtractFileDrive(ExpandConstant('{app}'));
  TmpDrive := ExtractFileDrive(ExpandConstant('{tmp}'));

  AppNeeded := StrToInt64('{#AppSize}') + ModelBytesToDownload() + Int64(SPACE_MARGIN_MB) * 1048576;
  TmpNeeded := 0;
  if not IsDesktopRuntimeInstalled() then
    TmpNeeded := Int64(RUNTIME_SPACE_MB) * 1048576;

  if CompareText(AppDrive, TmpDrive) = 0 then
  begin
    AppNeeded := AppNeeded + TmpNeeded;
    TmpNeeded := 0;
  end;
end;

// Returns '' when everything fits, otherwise a message for the user.
function CheckDiskSpace(): String;
var
  AppDrive, TmpDrive: String;
  AppNeeded, TmpNeeded: Int64;
begin
  GetSpaceNeeds(AppDrive, TmpDrive, AppNeeded, TmpNeeded);
  Result := CheckDrive(AppDrive, AppNeeded);
  if (Result = '') and (TmpNeeded > 0) then
    Result := CheckDrive(TmpDrive, TmpNeeded);
end;

// ---- Space display on the task page -------------------------------------------

var
  SpaceLabel: TNewStaticText;

// Shows the space needed for the current model selection and the free space.
// Called when the task page opens and on every click in the task list.
procedure UpdateSpaceLabel();
var
  AppDrive, TmpDrive: String;
  AppNeeded, TmpNeeded, FreeBytes, TotalBytes: Int64;
begin
  GetSpaceNeeds(AppDrive, TmpDrive, AppNeeded, TmpNeeded);
  if GetSpaceOnDisk64(AddBackslash(AppDrive), FreeBytes, TotalBytes) then
  begin
    SpaceLabel.Caption := Format('Disk space required on %s: %d MB (available: %d MB)', [
      AppDrive, BytesToMB(AppNeeded), BytesToMB(FreeBytes)]);
    if FreeBytes < AppNeeded then
      SpaceLabel.Font.Color := clRed
    else
      SpaceLabel.Font.Color := clWindowText;
  end
  else
    SpaceLabel.Caption := Format('Disk space required on %s: %d MB', [AppDrive, BytesToMB(AppNeeded)]);
end;

procedure TasksListClickCheck(Sender: TObject);
begin
  UpdateSpaceLabel();
end;

procedure InitializeWizard();
begin
  // Make room below the task list for the space line.
  WizardForm.TasksList.Height := WizardForm.TasksList.Height - ScaleY(24);

  SpaceLabel := TNewStaticText.Create(WizardForm);
  SpaceLabel.Parent := WizardForm.SelectTasksPage;
  SpaceLabel.Left := WizardForm.TasksList.Left;
  SpaceLabel.Top := WizardForm.TasksList.Top + WizardForm.TasksList.Height + ScaleY(8);
  SpaceLabel.Caption := '';

  WizardForm.TasksList.OnClickCheck := @TasksListClickCheck;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // The directory page counts only the application, because the models are chosen
  // afterwards; its figure would be misleading, so it is hidden.
  if CurPageID = wpSelectDir then
    WizardForm.DiskSpaceLabel.Visible := False
  else if CurPageID = wpSelectTasks then
    UpdateSpaceLabel();
end;

// ---- Setup lifecycle --------------------------------------------------------

// Asks up front whether a missing .NET runtime may be installed, before anything else
// happens. Declining (and every silent install) ends setup with a pointer to the download.
function InitializeSetup(): Boolean;
begin
  Result := True;
  // A suitable runtime is logged by HasVersion8OrNewer.
  if IsDesktopRuntimeInstalled() then
    Exit;

  Log('.NET Desktop Runtime 8 or newer: not found.');
  if SuppressibleMsgBox('WhisperDictation needs the .NET Desktop Runtime (version 8 or newer), which is not installed.'#13#10#13#10
       + 'Setup can download and install it for you. Windows will ask for administrator '
       + 'permission for this step only.'#13#10#13#10
       + 'Install the .NET 10 Desktop Runtime now?',
       mbConfirmation, MB_YESNO, IDNO) <> IDYES then
  begin
    SuppressibleMsgBox('Please install the .NET Desktop Runtime (version 8 or newer) from'#13#10
      + RUNTIME_PAGE + #13#10
      + 'and run this installer again.', mbInformation, MB_OK, IDOK);
    Result := False;
  end;
end;

// The task page is the first point where both the target folder and the model
// selection are known. Staying on the page lets the user deselect a model or go
// back and pick another folder. On silent installs, returning False exits setup.
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Error: String;
begin
  Result := True;
  if CurPageID <> wpSelectTasks then
    Exit;

  // The app cannot start without a Whisper model; an already installed one counts.
  if not WizardIsTaskSelected('whisperlarge') and not WizardIsTaskSelected('whispersmall') and
     ModelMissing('{#LargeName}') and ModelMissing('{#SmallName}') then
  begin
    SuppressibleMsgBox('Please select at least one Whisper model. The application needs one to work.',
      mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  Error := CheckDiskSpace();
  if Error <> '' then
  begin
    SuppressibleMsgBox(Error, mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Checked again before anything is downloaded, in case the free space changed meanwhile.
  Result := CheckDiskSpace();
  if Result <> '' then
    Exit;

  // The user agreed in InitializeSetup; setup would have ended otherwise.
  if not IsDesktopRuntimeInstalled() then
    Result := InstallRuntime();
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ModelsDir: String;
begin
  // Setup cannot create DestDir for skipped entries, so make sure the folder exists.
  if CurStep = ssInstall then
  begin
    ModelsDir := ExpandConstant('{app}\models');
    if not ForceDirectories(ModelsDir) then
      Log('Warning: could not create ' + ModelsDir);
  end;
end;
