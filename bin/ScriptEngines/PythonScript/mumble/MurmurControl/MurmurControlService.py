import sys, time
from BaseHTTPServer import BaseHTTPRequestHandler, HTTPServer
from MumbleChannelManager import ChannelManager

'''
    Makes possible to add and delete Murmur channels using http requests.
    
    request format: <server host:port>/<secret>/CREATE_CHANNEL/<channel name>
                    <server host:port>/<secret>/REMOVE_CHANNEL/<channel name>
                    
    Idea of the secret is that only scripts which know the <Secret> are able to 
    create or delete the channels
'''

SERVER_HOST_NAME = ''
SERVER_PORT_NUMBER = 9999

SECRET = "qwerty123" # REMEMBER TO CHANGE THIS !!!

MURMUR_HOST_NAME = "127.0.0.1"
MURMUR_PORT_NUMBER = 6502

class RequestHandler(BaseHTTPRequestHandler):

    def do_GET(self):
        from_address, from_port = self.client_address
        if self._is_remove_request() and self._is_secret_ok():
            full_name = self._get_full_channel_name()
            if full_name is not None:
                print(time.asctime() + " Remove request from %s: %s" % (from_address, full_name))
                self._channel_manager = ChannelManager(MURMUR_HOST_NAME, MURMUR_PORT_NUMBER)
                self._channel_manager.remove_channel(full_name)
                self.send_response(200) 
                self.send_header("Content-type", "text/html")
                self.end_headers()
                self.wfile.write("OK")
                return
            
        if self._is_create_request() and self._is_secret_ok():
            full_name = self._get_full_channel_name()
            
            if full_name is not None:
                print(time.asctime() + " Create request from %s: %s" % (from_address, full_name))
                self._channel_manager = ChannelManager(MURMUR_HOST_NAME, MURMUR_PORT_NUMBER)
                self._channel_manager.get_or_create_channel(full_name)
                self.send_response(200) 
                self.send_header("Content-type", "text/html")
                self.end_headers()
                self.wfile.write("OK")
                return
            
        print(time.asctime() + " Bad request from %s: %s" % (from_address, self.path))
        self.send_response(404)
        self.send_header("Content-type", "text/html")
        self.end_headers()
        self.wfile.write("FAIL")
        
    def _is_secret_ok(self):
        '''return True if the <secret> was corrent. Otherwise return false
           @param s http request
        '''
        try:
            secret = self.path.strip('/').split('/')[0]
            if secret == SECRET:
                return True
            else:
                return False
        except:
            return False
            
    def _is_remove_request(self):
        ''' return True if the request was REMOVE_CHANNEL otherwise return False
            @param s http request
        '''   
        try:
            command = self.path.strip('/').split('/')[1]
            if command == "REMOVE_CHANNEL":
                return True
            else:
                return False
        except:
            return False

    def _is_create_request(self):
        ''' return True if the request was REMOVE_CHANNEL otherwise return False
            @param s http request
        '''   
        try:
            command = self.path.strip('/').split('/')[1]
            if command == "CREATE_CHANNEL":
                return True
            else:
                return False
        except:
            return False
            
    def _get_full_channel_name(self):        
        ''' Return the full channel name in the request.
            if channel name cannot parsed from the request then return None
        '''
        try:
            parts =  self.path.strip('/').split('/')
            full_name = None
            for i in range(2, len(parts)):
                if full_name is None:
                    full_name = parts[i]
                else:
                    full_name = full_name + "/" + parts[i]
            return full_name
        except:
            return None
            
    def log_message(format, *args):
        ''' to disable default log messages'''
        return
    
        

class Service():

    def __init__(self):
        self._httpd = HTTPServer((SERVER_HOST_NAME, SERVER_PORT_NUMBER), RequestHandler)
        
    def run(self):
        try:
            self._httpd.serve_forever()
        except KeyboardInterrupt:
            pass
        self._httpd.server_close()
        
if __name__ == '__main__':
    print ("To create and delete channels:")
    print ("  http://%s:%s/%s/CREATE_CHANNEL/MyChannel/MySubChannel" % (SERVER_HOST_NAME, SERVER_PORT_NUMBER, SECRET))
    print ("  http://%s:%s/%s/REMOVE_CHANNEL/MyChannel/MySubChannel" % (SERVER_HOST_NAME, SERVER_PORT_NUMBER, SECRET))
    print ("")
    print time.asctime(), "Service Starts - %s:%s" % (SERVER_HOST_NAME, SERVER_PORT_NUMBER)
    print ("Press CRTL-C to quit service.")
    service = Service()
    service.run()
    print time.asctime(), "Service Stops - %s:%s" % (SERVER_HOST_NAME, SERVER_PORT_NUMBER)
    