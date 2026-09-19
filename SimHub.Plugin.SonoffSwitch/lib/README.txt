This folder is only used if you switch the project to the "portable" reference mode.

By default, the project references SimHub.Plugins.dll, GameReaderCommon.dll, and
log4net.dll straight out of your SimHub install folder (see SimHubInstallPath in
SimHub.Plugin.SonoffSwitch.csproj). That's the easiest option and needs nothing placed here.

If you'd rather not depend on a fixed install path (e.g. building on a machine without
SimHub installed), copy these three files from your SimHub folder into this lib folder:

  SimHub.Plugins.dll
  GameReaderCommon.dll
  log4net.dll

...and edit the <HintPath> entries in SimHub.Plugin.SonoffSwitch.csproj to point at
"lib\SimHub.Plugins.dll" etc. instead of $(SimHubInstallPath)\...
