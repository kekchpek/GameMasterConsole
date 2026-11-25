#!/usr/bin/env python3
"""
Game Master Console Client

A Python console application that discovers GameMaster servers via SSDP protocol
and allows sending commands to them.

Usage:
    python gamemaster_client.py
    python gamemaster_client.py --server http://localhost:54345

Commands:
    EXIT - Close the application
    RESTART - Restart server discovery
    <COMMAND> <ARG1> <ARG2> ... - Send command to GameMaster server
"""

import argparse
import socket
import struct
import threading
import time
import json
import requests
import urllib.parse
from typing import List, Dict, Optional, Tuple
import xml.etree.ElementTree as ET
import re
import sys
import logging
from collections import OrderedDict

from gamemaster_protocol import DEFAULT_SERVER_URL, command_payload

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)


def create_command_reader():
    if not sys.stdin.isatty() or not sys.stdout.isatty():
        return input

    from prompt_toolkit import prompt
    from prompt_toolkit.history import InMemoryHistory

    history = OrderedDict()

    def read_command(message):
        command = prompt(message, history=InMemoryHistory(list(history))).strip()
        if command:
            history.pop(command, None)
            history[command] = None
        return command

    return read_command


def parse_server_url(value: str) -> str:
    try:
        url = urllib.parse.urlsplit(value)
        if (url.scheme == 'http' and url.hostname and url.port != 0
                and url.username is None and url.path in ('', '/')
                and not url.query and not url.fragment):
            return f'http://{url.netloc}'
    except ValueError:
        pass
    raise argparse.ArgumentTypeError('use an HTTP base URL such as http://localhost:54345')


def post_command(base_url, command, args):
    return requests.post(f'{base_url}/command', json=command_payload(command, args), timeout=10)


class SSDPClient:
    """Client for discovering GameMaster servers via SSDP protocol"""
    
    MULTICAST_GROUP = '239.255.255.250'
    MULTICAST_PORT = 1900
    SERVICE_TYPE = 'urn:schemas-armor-guild:service:GameMaster:1'
    
    def __init__(self, timeout: float = 5.0):
        self.timeout = timeout
        self.discovered_servers = []
        self.stop_discovery = False
        
    def create_msearch_request(self, search_target: str = None) -> str:
        """Create M-SEARCH request for SSDP discovery"""
        if search_target is None:
            search_target = self.SERVICE_TYPE
            
        return (
            f"M-SEARCH * HTTP/1.1\r\n"
            f"HOST: {self.MULTICAST_GROUP}:{self.MULTICAST_PORT}\r\n"
            f"MAN: \"ssdp:discover\"\r\n"
            f"ST: {search_target}\r\n"
            f"MX: 3\r\n"
            f"\r\n"
        )
    
    def parse_ssdp_response(self, response: str) -> Optional[Dict[str, str]]:
        """Parse SSDP response to extract server information"""
        try:
            lines = response.strip().split('\r\n')
            if not lines[0].startswith('HTTP/1.1 200'):
                return None
                
            headers = {}
            for line in lines[1:]:
                if ':' in line:
                    key, value = line.split(':', 1)
                    headers[key.strip().upper()] = value.strip()
                    
            # Extract location URL
            location = headers.get('LOCATION')
            if not location:
                return None
                
            # Parse the location URL to get IP and port
            parsed_url = urllib.parse.urlparse(location)
            if not parsed_url.hostname:
                return None
                
            return {
                'ip': parsed_url.hostname,
                'port': parsed_url.port or 80,
                'location': location,
                'server': headers.get('SERVER', 'Unknown'),
                'usn': headers.get('USN', ''),
                'st': headers.get('ST', ''),
                'computer_name': headers.get('COMPUTER-NAME', ''),
                'cache_control': headers.get('CACHE-CONTROL', '')
            }
        except Exception as e:
            logger.debug(f"Error parsing SSDP response: {e}")
            return None
    
    def get_server_description(self, server_info: Dict[str, str]) -> Optional[Dict[str, str]]:
        """Get server description from the description XML"""
        try:
            response = requests.get(server_info['location'], timeout=3)
            if response.status_code == 200:
                # Parse XML to extract device information
                root = ET.fromstring(response.text)
                
                # Find device info
                device = root.find('.//{urn:schemas-upnp-org:device-1-0}device')
                if device is not None:
                    friendly_name = device.find('.//{urn:schemas-upnp-org:device-1-0}friendlyName')
                    model_name = device.find('.//{urn:schemas-upnp-org:device-1-0}modelName')
                    
                    server_info['friendly_name'] = friendly_name.text if friendly_name is not None else 'GameMaster Server'
                    server_info['model_name'] = model_name.text if model_name is not None else 'Unknown'
                    
                return server_info
        except Exception as e:
            logger.debug(f"Error getting server description: {e}")
            
        return server_info
    
    def discover_servers(self, search_time: float = 5.0) -> List[Dict[str, str]]:
        """Discover GameMaster servers on the network"""
        self.discovered_servers = []
        self.stop_discovery = False
        
        try:
            # Create UDP socket
            sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            sock.settimeout(1.0)
            
            # Send M-SEARCH requests for essential search targets
            search_targets = [
                self.SERVICE_TYPE,
                'ssdp:all'
            ]
            
            multicast_addr = (self.MULTICAST_GROUP, self.MULTICAST_PORT)
            
            # Start listening thread
            listen_thread = threading.Thread(target=self._listen_for_responses, args=(sock,))
            listen_thread.daemon = True
            listen_thread.start()
            
            # Send M-SEARCH requests periodically
            start_time = time.time()
            while time.time() - start_time < search_time and not self.stop_discovery:
                for target in search_targets:
                    try:
                        request = self.create_msearch_request(target)
                        sock.sendto(request.encode('utf-8'), multicast_addr)
                        logger.debug(f"Sent M-SEARCH for {target}")
                    except Exception as e:
                        logger.debug(f"Error sending M-SEARCH: {e}")
                
                time.sleep(1)
            
            self.stop_discovery = True
            sock.close()
            
            # Get detailed information for discovered servers
            detailed_servers = []
            for server in self.discovered_servers:
                detailed_server = self.get_server_description(server)
                if detailed_server:
                    detailed_servers.append(detailed_server)
            
            # Group servers by USN to combine multiple IPs for the same server
            unique_servers = []
            seen_usns = set()
            seen_ips = set()
            
            for server in detailed_servers:
                usn = server.get('usn', '')
                if usn:
                    # Group by USN to handle same server on multiple network interfaces
                    if usn not in seen_usns:
                        seen_usns.add(usn)
                        # Find all IPs for this server (same USN)
                        server_ips = [s['ip'] for s in detailed_servers if s.get('usn', '') == usn]
                        server['all_ips'] = server_ips
                        unique_servers.append(server)
                else:
                    # Fallback to IP/port for servers without USN
                    key = (server['ip'], server['port'])
                    if key not in seen_ips:
                        seen_ips.add(key)
                        server['all_ips'] = [server['ip']]
                        unique_servers.append(server)
            
            return unique_servers
            
        except Exception as e:
            logger.error(f"Error during server discovery: {e}")
            return []
    
    def _listen_for_responses(self, sock: socket.socket):
        """Listen for SSDP responses in a separate thread"""
        while not self.stop_discovery:
            try:
                data, addr = sock.recvfrom(1024)
                response = data.decode('utf-8', errors='ignore')
                
                server_info = self.parse_ssdp_response(response)
                if server_info and self._is_gamemaster_server(server_info):
                    # Check if we already have this server by USN (to handle multiple IPs for same server)
                    usn = server_info.get('usn', '')
                    if usn and not any(s.get('usn', '') == usn for s in self.discovered_servers):
                        self.discovered_servers.append(server_info)
                        computer_name = server_info.get('computer_name', 'Unknown')
                        logger.debug(f"Discovered server: {server_info['ip']}:{server_info['port']} ({computer_name})")
                    elif not usn:
                        # Fallback to IP/port check if no USN available
                        if not any(s['ip'] == server_info['ip'] and s['port'] == server_info['port'] 
                                 for s in self.discovered_servers):
                            self.discovered_servers.append(server_info)
                            computer_name = server_info.get('computer_name', 'Unknown')
                            logger.debug(f"Discovered server: {server_info['ip']}:{server_info['port']} ({computer_name})")
                        
            except socket.timeout:
                continue
            except Exception as e:
                if not self.stop_discovery:
                    logger.debug(f"Error listening for responses: {e}")
                break
    
    def _is_gamemaster_server(self, server_info: Dict[str, str]) -> bool:
        """Check if the server is a GameMaster server based on response headers"""
        st = server_info.get('st', '').lower()
        usn = server_info.get('usn', '').lower()
        server = server_info.get('server', '').lower()
        
        return (
            self.SERVICE_TYPE.lower() in st or
            'gamemaster' in usn or
            'gamemaster' in server
        )


class GameMasterClient:
    """Client for communicating with GameMaster servers"""
    
    def __init__(self):
        self.current_server = None
        self.ssdp_client = SSDPClient()

    def select_server(self, server_url: Optional[str]) -> bool:
        self.current_server = None
        if server_url is None:
            return self.connect_to_server(DEFAULT_SERVER_URL, timeout=1) or self.discover_and_select_server()

        if self.connect_to_server(server_url, timeout=3):
            return True

        print(f"❌ Cannot connect to a GameMaster server at {server_url}")
        return False

    def connect_to_server(self, server_url: str, timeout: float) -> bool:
        try:
            response = requests.get(server_url + '/', timeout=timeout)
            response.raise_for_status()
            status = response.json()
        except (requests.exceptions.RequestException, ValueError):
            return False

        if (not isinstance(status, dict) or status.get('Status') != 'Running'
                or not isinstance(status.get('RegisteredCommands'), list)):
            return False

        url = urllib.parse.urlsplit(server_url)
        self.current_server = {'ip': url.hostname, 'port': url.port or 80, 'base_url': server_url}
        print(f"✅ Connected to GameMaster server at {server_url}")
        self.send_command('help', [])
        return True
        
    def discover_and_select_server(self) -> bool:
        """Discover servers and let user select one"""
        print("🔍 Searching for GameMaster servers...")
        
        # Start with short discovery, continue if no servers found
        search_time = 3.0
        servers = []
        
        while not servers:
            servers = self.ssdp_client.discover_servers(search_time)
            
            if not servers:
                print(f"No servers found in {search_time:.1f}s. Continuing search...")
                search_time = min(search_time + 2.0, 10.0)  # Increase search time, max 10s
                
                # Ask user if they want to continue
                try:
                    user_input = input("Press Enter to continue searching or 'q' to quit: ").strip().lower()
                    if user_input == 'q':
                        return False
                except KeyboardInterrupt:
                    print("\nSearch cancelled.")
                    return False
            
        if len(servers) == 1:
            # Single server found
            server = servers[0]
            self.current_server = server
            computer_name = server.get('computer_name', 'Unknown Computer')
            all_ips = server.get('all_ips', [server['ip']])
            
            if len(all_ips) > 1:
                print(f"✅ Found GameMaster server: {computer_name} "
                      f"at {server['ip']}:{server['port']} (and {len(all_ips)-1} other interface(s))")
            else:
                print(f"✅ Found GameMaster server: {computer_name} "
                      f"at {server['ip']}:{server['port']}")
            
            self.send_command("help", [])
            return True
            
        elif len(servers) > 1:
            # Multiple servers found, let user select
            print(f"\n📡 Found {len(servers)} GameMaster servers:")
            for i, server in enumerate(servers, 1):
                computer_name = server.get('computer_name', 'Unknown Computer')
                all_ips = server.get('all_ips', [server['ip']])
                
                if len(all_ips) > 1:
                    ip_info = f"{server['ip']}:{server['port']} (+ {len(all_ips)-1} more interfaces)"
                else:
                    ip_info = f"{server['ip']}:{server['port']}"
                
                print(f"  {i}. {computer_name} at {ip_info}")
            
            while True:
                try:
                    choice = input(f"\nSelect server (1-{len(servers)}): ").strip()
                    if choice.isdigit():
                        index = int(choice) - 1
                        if 0 <= index < len(servers):
                            self.current_server = servers[index]
                            computer_name = self.current_server.get('computer_name', 'Unknown Computer')
                            print(f"✅ Selected server: {computer_name} "
                                  f"at {self.current_server['ip']}:{self.current_server['port']}")
                            return True
                    print("Invalid selection. Please try again.")
                except KeyboardInterrupt:
                    print("\nSelection cancelled.")
                    return False
                    
        return False
    
    def send_command(self, command: str, args: List[str]) -> bool:
        """Send command to the current server"""
        if not self.current_server:
            print("❌ No server selected. Use RESTART to discover servers.")
            return False
            
        try:
            base_url = self.current_server.get('base_url') or f"http://{self.current_server['ip']}:{self.current_server['port']}"
            response = post_command(base_url, command, args)
            
            if response.status_code == 200:
                try:
                    result = response.json()
                    if result.get('Success', False):
                        print(f"✅ {result.get('Message', 'Command executed successfully')}")
                        
                        # Show additional result data if available
                        data = result.get('Data', {})
                        if data:
                            for key, value in data.items():
                                if key not in ['command', 'arguments', 'timestamp', 'status']:
                                    print(f"   {key}: {value}")
                    else:
                        print(f"❌ Command failed: {result.get('Message', 'Unknown error')}")
                except json.JSONDecodeError:
                    print(f"✅ Command sent successfully. Response: {response.text}")
                    
                return True
            else:
                print(f"❌ Server error ({response.status_code}): {response.text}")
                return False
                
        except requests.exceptions.ConnectionError:
            print(f"❌ Cannot connect to server {self.current_server['ip']}:{self.current_server['port']}")
            return False
        except requests.exceptions.Timeout:
            print("❌ Request timeout. Server may be busy.")
            return False
        except Exception as e:
            print(f"❌ Error sending command: {e}")
            return False
    
    def run(self, server_url: Optional[str] = None):
        """Main application loop"""
        print("🎮 GameMaster Console Client")
        print("=" * 40)
        print("Commands:")
        print("  EXIT     - Close application")
        print("  RESTART  - Reconnect or repeat server discovery")
        print("  <CMD> <ARGS> - Send command to server")
        print("  Up / Down - Browse command history")
        print("=" * 40)
        
        if not self.select_server(server_url):
            print("No servers available. Exiting.")
            return
            
        read_command = create_command_reader()
        # Main command loop
        while True:
            try:
                # Get command input
                if self.current_server:
                    computer_name = self.current_server.get('computer_name', 'Unknown')
                    prompt = f"GM[{computer_name}@{self.current_server['ip']}:{self.current_server['port']}]> "
                else:
                    prompt = "GM[No Server]> "
                    
                user_input = read_command(prompt).strip()
                
                if not user_input:
                    continue
                    
                # Parse command and arguments
                parts = user_input.split()
                command = parts[0]
                args = parts[1:] if len(parts) > 1 else []
                
                # Handle client-side commands
                if command.upper() == 'EXIT':
                    print("👋 Goodbye!")
                    break
                elif command.upper() == 'RESTART':
                    print("🔄 Selecting a GameMaster server...")
                    if not self.select_server(server_url):
                        print("No servers available.")
                else:
                    # Send command to server
                    self.send_command(command, args)
                    
            except KeyboardInterrupt:
                print("\n👋 Goodbye!")
                break
            except EOFError:
                print("\n👋 Goodbye!")
                break


def main():
    """Entry point for the application"""
    parser = argparse.ArgumentParser(description='Connect to a GameMaster Console server.')
    parser.add_argument('--server', type=parse_server_url, help='connect directly to this HTTP base URL')
    options = parser.parse_args()
    try:
        client = GameMasterClient()
        client.run(options.server)
    except Exception as e:
        logger.error(f"Unexpected error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
