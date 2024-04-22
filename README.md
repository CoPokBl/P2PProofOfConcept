# E2E P2P Chat App
This is a chat app that implements end-to-end encryption, and peer to peer connections via NAT holepunching using the 'Holepuncher' project.

## Holepuncher
A UDP server that lets clients create 'rooms' with a room code and then another client can use the code to join, both clients are then sent each others IPEndPoints.

## Chat Peer
A client which can either host or join a room. It then establishes a connection with the other client and both clients can send UDP packets to have a conversation.
