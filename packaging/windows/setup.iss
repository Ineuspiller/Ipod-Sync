; Instalador de IpodSync para Windows, con Inno Setup (https://jrsoftware.org/isinfo.php).
;
; Se compila en CI contra la salida de `dotnet publish` (ver
; .github/workflows/release.yml), que para win-x64 ya es un solo .exe
; autocontenido (ver EnableCompressionInSingleFile/
; IncludeNativeLibrariesForSelfExtract en el csproj/workflow). El instalador
; no reduce ese peso: lo que agrega es la experiencia normal de instalar en
; Windows -acceso directo, desinstalador registrado- en vez de "descomprimí
; una carpeta a mano".
;
; MyAppVersion se pisa desde CI con /DMyAppVersion=X.Y.Z (el tag del
; release); el valor de acá es solo el default para compilar en local.
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#define MyAppName "IpodSync"
#define MyAppPublisher "Ineuspiller"
#define MyAppURL "https://github.com/Ineuspiller/Ipod-Sync"
#define MyAppExeName "IpodSync.exe"
; Fijo entre versiones para que Windows trate cada release como una
; actualización del mismo programa (mismo lugar en "Aplicaciones", el
; instalador nuevo reemplaza al viejo) y no como algo distinto. Sin llaves:
; se las agrega el escape {{...}} de abajo. Una llave literal sin escapar
; ({GUID}) haría que Inno Setup intente resolverla como una constante suya
; y fallara al compilar.
#define MyAppId "BE15A505-2A4A-413B-B02A-75877B3EFA52"

[Setup]
AppId={{{#MyAppId}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
VersionInfoVersion={#MyAppVersion}

; Por usuario, no por máquina: no hace falta ser administrador para
; instalar. PrivilegesRequiredOverridesAllowed deja elegir "para todos los
; usuarios" a quien sí tenga esos permisos y lo prefiera.
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Se sobreescribe con /O desde CI para que el .exe salga con el nombre y en
; la carpeta que espera el paso de "subir al Release" del workflow.
OutputDir=.
OutputBaseFilename=IpodSync-Setup-{#MyAppVersion}

SetupIconFile=..\..\IpodSync.Desktop\Assets\ipodsync.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; El contenido de dist\win-x64\ (publicado por el workflow de release antes
; de este paso). "*" y no un archivo puntual porque, aunque hoy el publish
; es un solo .exe, no hay que romper el instalador si eso cambia el día de
; mañana (un future PublishTrimmed que reintroduzca alguna nativa suelta,
; por ejemplo).
[Files]
Source: "..\..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; El arranque automático NO se configura acá: es una opción dentro de la
; propia app (ventana de Settings, "Arrancar con el sistema"), para que
; quede en un solo lugar y no como dos interruptores separados para lo
; mismo entre el instalador y la app.
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
