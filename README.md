# ScpSlAntiDDoS 
### Simple anti-ddos plugin using NW-API
Prevents logging of [STDOUT] [NM] DataReceived: bad! messages. Will count the number of bad packets recieved during the round and output the number and total data size to the console and to a Discord Web Hook if one is provided in the config.

This plugin cant stop your network or even the game from being overloaded by a DDoS if its large enough. Using a provider that has DDoS protection built-in and then setting up a firewall is the most effective way to stop DDoS attacks. This plugin gives you an idea of how bad an attack is and whether an attack is reaching the game as setting up your server like explained will make it so no bad packets ever reach the game. 
