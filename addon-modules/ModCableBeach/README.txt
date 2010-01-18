
Setup
================================================================================

* You will need to download and compile Cable Beach from
  <http://cablebeach.googlecode.com/>. Edit the WorldServer.ini and
  InventoryServer.ini files to suit your needs.
* You will need a recent version of OpenSim. Anything after 2008/08/05 is fine.
* Make sure ModCableBeach has been checked out into the OpenSim directory
  structure, under addon-modules/ModCableBeach.
* Run OpenSim's runprebuild.bat or runprebuild.sh and recompile OpenSim.
* Copy addon-modules/ModCableBeach/config-include/CableBeach.ini to OpenSim's
  bin/config-include/ directory.
* Open your OpenSim.ini file. Make sure it is properly setup to run in grid mode
  before making any changes. At the bottom of the config file, comment out all
  of the Include-* lines (such as Include-Grid) and add the following line:
    Include-CableBeach   = "config-include/CableBeach.ini"

Running
================================================================================

* Start by initializing the required OpenSim grid services. These are
  OpenSim.Grid.GridServer.exe and optionally OpenSim.Grid.MessagingServer.exe.
  Do not start any of the other OpenSim grid services.
* Start the Cable Beach services InventoryServer.exe, followed by
  WorldServer.exe.
* Start the OpenSim.exe process. Look for logging lines that start with
  "[CABLE BEACH ...]" and communication with the Cable Beach services to ensure
  that ModCableBeach.dll was successfully loaded

Using
================================================================================

You should now be able to login using whichever login services you configured in
the Cable Beach WorldServer. The login method and specifics of the login flow
are dependent on the WorldServer extensions that are enabled and the contents of
the service requirements file (WorldServer.Services.txt).
