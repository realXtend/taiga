import urllib
import rxactor

'''
This script ensures that there is a mumble channel existing for the EC_VoiceChannel component.

When channelid attribute of EC_VoiceChannel component is changed the old channel is removed.
'''

SERVER_ADDRESS = "127.0.0.1:9999" # address of computer running MurmurControl/MurmurControlService.py
SECRET = "qwerty123" # REMEMBER TO CHANGE THIS !!! @see MurmurControl/MurmurControlService.py

CREATE_BASE_URL = "http://"+SERVER_ADDRESS+"/"+SECRET+"/CREATE_CHANNEL/" #todo get from ini
REMOVE_BASE_URL = "http://"+SERVER_ADDRESS+"/"+SECRET+"/REMOVE_CHANNEL/" #todo get from ini

class ChannelChecker(rxactor.Actor):
    @staticmethod
    def GetScriptClassName():
        return "voicechannel.ChannelChecker"
    
    def EventCreated(self):
        self._cleanup_url = None
        self.SetTimer(0.5, True) # We poll EC_VoiceChannel for channelid attribute
        self.create_channel()
        # @todo replace polling with proper event handlers
        #rex_objects = self.MyWorld.CS.World.EventManager.OnClientConnect += self.clientConnectedHandle
        
    def EventDestroyed(self):
        self.cleanup()
                    
    def create_channel(self):
        ec = self.rexGetECAttributes("EC_VoiceChannel")
        if ec is not None:
            cleanup_url = REMOVE_BASE_URL + str( ec["channelid"] )
            if self._cleanup_url != cleanup_url:
                url = CREATE_BASE_URL + str( ec["channelid"] )
                urllib.urlopen(url)
                self.cleanup()
                self._cleanup_url = cleanup_url
    
    def EventTimer(self):
       self.create_channel()
        
    def cleanup(self):
        if self._cleanup_url is not None:
            urllib.urlopen(self._cleanup_url)
            self._cleanup_url = None

    # @todo replace polling with proper event handlers
    #def clientConnectedHandle(self, client_core):
    #   client_core.OnPrimFreeData += self.onPrimFreeData
        
    #def onPrimFreeData(self, client, data):
    #   self.create_channel()

    
        