import sys
import Ice
import Murmur_ice

class ChannelManager:
    '''
        All channels created by this script are marked by setting description field to 'EC_VoiceChannel'
        By calling 'remove_channel' onl subchannels marked as 'EC_VoiceChannel' are removed
        
        @todo: Channel name validator
    '''
    ROOT_ID = -1
    def __init__(self, address, port):
        ic = Ice.initialize(sys.argv)
        base = ic.stringToProxy("Meta:tcp -h "+str(address)+" -p "+str(port))
        meta_proxy = Murmur_ice._M_Murmur.MetaPrx.checkedCast(base)
        list = meta_proxy.getBootedServers()
        self._server_proxy = list[0]
        
    def get_or_create_channel(self, full_name):
        channel = self._get_channel_by_full_name(full_name)
        if channel is None:
            self._create_channel(full_name)
    
    def remove_channel(self, full_name):
        names = full_name.split('/')
        for depth in range(0,len(names)):
            name = names[depth]
            sub_channel_name = ""
            for i in range(0, len(names) - depth):
                if i == 0:
                    sub_channel_name = names[i] 
                else:
                    sub_channel_name = sub_channel_name + '/'+ names[i] 
            channel = self._get_channel_by_full_name(sub_channel_name)
            if channel is not None and channel.description == "EC_VoiceChannel" and len(self._get_children(channel)) == 0:
                self._server_proxy.removeChannel(channel.id)
                
    def _get_children(self, channel):
        children = []
        channels = self._server_proxy.getChannels().values()
        for c in channels:
            if c.parent == channel.id:
                children.append(c)
        return children        
            
        
    def _get_channel_by_full_name(self, full_name):
        '''
            return a channel with parent channels descriped by given 'full_name'
        '''
        names = full_name.split('/')
        tree = self._server_proxy.getTree()
        root = tree.c
        children = tree.children
        channel = None
        for depth in range(0,len(names)):
            name = names[depth]
            channel = None
            for child in children:
                if child.c.name == name:
                    channel = child.c
                    break
            if channel is None:
                return None
            children = child.children    
        return channel    
        
    def _create_channel(self, full_name):
        ''' Created all necessary channels
            eg. "channelA/ChannelB" -> channelA, channelB
        '''
        names = full_name.split('/')
        tree = self._server_proxy.getTree()
        root = tree.c
        children = tree.children
        next_parent_id = tree.c.id
        for depth in range(0,len(names)):
            name = names[depth]
            channel = None
            for child in children:
                if child.c.name == name:
                    channel = child.c
                    next_parent_id = channel.id
                    break
            if channel is None:
                id = self._server_proxy.addChannel(name, next_parent_id)
                new_channel = self._server_proxy.getChannels()[id]
                if new_channel is not None:
                    new_channel.temporary = True # DOESN'T WORK!
                    new_channel.description = "EC_VoiceChannel"
                    self._server_proxy.setChannelState(new_channel)
                if depth < len(names)-1:
                    self._create_channel(full_name) # recursive call !
                    return
            children = child.children    