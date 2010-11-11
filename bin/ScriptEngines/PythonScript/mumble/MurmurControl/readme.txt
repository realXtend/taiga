If you want to enabled automatic mumble channel management:

* run MurmurControlService.py script.
  - this must be run at the same computer than murmur server is running
  - This starts http server and creates and removes channels according requests from 'voicechannel.py' script.
* set rex property 'ServerScriptClass' to 'voicechannel.ChannelChecker' for the object having EC_VoiceChannel component

