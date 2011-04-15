﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Common.Util;
using Common.Beans;
using System.Xml;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Remoting.Channels;
using System.Net.Sockets;
using Common.Slots;
using Common.Services;
using PuppetMaster;
using System.Collections;
using Server.Services;
using System.Threading;
using System.Runtime.Remoting.Messaging;

namespace Server
{
    public interface IServer
    {
        void Init();
    }

    class callback
    {
        public delegate bool RemoteAsyncDelegate();
        public delegate ClientMetadata RemoteLookupDelegate();
        public bool _status;
        public ClientMetadata data;
        public ManualResetEvent waiter = new ManualResetEvent(false);

        // This is the call that the AsyncCallBack delegate will reference.
        public void OurRemoteAsyncCallBack(IAsyncResult ar)
        {
            RemoteAsyncDelegate del = (RemoteAsyncDelegate)((AsyncResult)ar).AsyncDelegate;
            _status = del.EndInvoke(ar);
            waiter.Set();
            return;
        }

        public void OurLookupAsyncCallBack(IAsyncResult ar)
        {
            RemoteLookupDelegate del = (RemoteLookupDelegate)((AsyncResult)ar).AsyncDelegate;
            data = del.EndInvoke(ar);
            waiter.Set();
            return;
        }
    }

    //TODO: maybe it's better to implement lookup service in a separate manager
    //it is here just for the purpose of testing
    public class Server : MarshalByRefObject, IServer, IServerFacade, ILookupService
    {

        private string _username;
        private int _port;
        private string _puppetIP;
        private int _puppetPort;
        private List<ServerMetadata> _servers;
        private string _configFile;
        private string _path;
        ServerAction action;

        //TODO: Temporary vars, move to some separate manager later
        private int _sequenceNumber;
       


        private bool _isOnline;

        public Server(string filename)
        {
            _sequenceNumber = 0;
            action = new ServerAction(this._username);
            ReadConfigurationFile(filename);
        }

        public Server(string username, int port, string path, string configFile)
        {
            _username = username;
            _port = port;
            _path = path;
            _configFile = _path + configFile;
            _sequenceNumber = 0;
            _clients = new Dictionary<string, ClientMetadata>();
            ReadConfigurationFile();
            action = new ServerAction(_username);
            
        }

        //Deprecated
        private void ReadConfigurationFile(string filename)
        {
            string current_dir = System.IO.Directory.GetCurrentDirectory();
            XmlDocument xmlDoc = new XmlDocument(); //* create an xml document object.
            xmlDoc.Load(filename); //* load the XML document from the specified file.

            XmlNodeList usernamelist = xmlDoc.GetElementsByTagName("Username");
            XmlNodeList portlist = xmlDoc.GetElementsByTagName("Port");
            XmlNodeList puppetmasteriplist = xmlDoc.GetElementsByTagName("PuppetMasterIP");
            XmlNodeList puppetmasterportlist = xmlDoc.GetElementsByTagName("PuppetMasterPort");
            XmlNodeList serverslist = xmlDoc.GetElementsByTagName("Server");

            _username = usernamelist[0].InnerText;
            _port = Convert.ToInt32(portlist[0].InnerText);
            _puppetIP = puppetmasteriplist[0].InnerText;
            _puppetPort = Convert.ToInt32(puppetmasterportlist[0].InnerText);
            _servers = new List<ServerMetadata>();

            for (int i = 0; i < 2; i++)
            {
                XmlNodeList server_ipportlist = serverslist[i].ChildNodes;
                string id = server_ipportlist[0].InnerText;
                string ip_addr = server_ipportlist[1].InnerText;
                int port = Convert.ToInt32(server_ipportlist[2].InnerText);
                ServerMetadata sm = new ServerMetadata();
                sm.Username = id;
                sm.IP_Addr = ip_addr;
                sm.Port = port;
                _servers.Add(sm);
            }
        }


        private void ReadConfigurationFile()
        {
            string current_dir = System.IO.Directory.GetCurrentDirectory();
            XmlDocument xmlDoc = new XmlDocument(); //* create an xml document object.
            xmlDoc.Load(_configFile); //* load the XML document from the specified file.

            XmlNodeList puppetmasteriplist = xmlDoc.GetElementsByTagName("PuppetMasterIP");
            XmlNodeList puppetmasterportlist = xmlDoc.GetElementsByTagName("PuppetMasterPort");
            XmlNodeList serverslist = xmlDoc.GetElementsByTagName("Server");

            _puppetIP = puppetmasteriplist[0].InnerText;
            _puppetPort = Convert.ToInt32(puppetmasterportlist[0].InnerText);
            _servers = new List<ServerMetadata>();

            

            for (int i = 0; i < 3; i++)
            {
                
                XmlNodeList server_ipportlist = serverslist[i].ChildNodes;
                string id = server_ipportlist[0].InnerText;
                if (_username.Equals(id))
                {
                    continue;
                }
                else
                {
                    string ip_addr = server_ipportlist[1].InnerText;
                    int port = Convert.ToInt32(server_ipportlist[2].InnerText);
                    ServerMetadata sm = new ServerMetadata();
                    sm.Username = id;
                    sm.IP_Addr = ip_addr;
                    sm.Port = port;
                    _servers.Add(sm);
                }
            }
        }

        /*
         * Implements IServer
         */

        void IServer.Init()
        {
            RegisterChannel();
            //StartFacade(); //Cannot register two interfaces of the same object, so not exposing the facade services for now...
            StartServices(); //Should be done by the Connect method
            StartConsistencyService();
            NotifyPuppetMaster();
        }

        private void RegisterChannel()
        {
            IDictionary RemoteChannelProperties = new Dictionary<string, string>();
            RemoteChannelProperties["port"] = _port.ToString();
            RemoteChannelProperties["name"] = _username;
            TcpChannel channel = new TcpChannel(RemoteChannelProperties, null, null);
            ChannelServices.RegisterChannel(channel, true);
        }

        void StartFacade()
        {

            //Facade Service
            string serviceName = _username + "/" + Common.Constants.SERVER_FACADE_SERVICE;
            Helper.StartService(_username, _port, serviceName, this, typeof(IServerFacade));
        }

        void StartConsistencyService()
        {
            string serviceName = _username + "/" + Common.Constants.CONSISTENCY_SERVICE_NAME;
            Helper.StartService(_username, _port, serviceName, action , typeof(IConsistencyService));
        }

        void StartServices()
        {
            //Lookup Service
            //TODO: should have a separate object for this later on
            string serviceName = _username + "/" + Common.Constants.LOOKUP_SERVICE_NAME;
            Helper.StartService(_username, _port, serviceName, this, typeof(ILookupService));
        }

        void StopServices()
        {
            //Lookup Service
            Helper.StopService(_username, "Booking service", this);
        }

        private void NotifyPuppetMaster()
        {

            String connectionString = "tcp://" + _puppetIP + ":" + _puppetPort + "/" + Common.Constants.PUPPET_MASTER_SERVICE_NAME;

            PuppetMasterService pms = (PuppetMasterService)Activator.GetObject(
                typeof(PuppetMasterService),
                connectionString);
            
            try
            {
                Log.Show(_username, "Trying to connect to Puppet Master on: " + connectionString);
                pms.registerServer(_username, Helper.GetIPAddress(), _port);
                Log.Show(_username, "Sucessfully registered client on Pupper Master.");
                System.Console.ReadLine();
            }
            catch (SocketException)
            {
                Log.Show(_username, "Unable to connect to Puppet Master.");
            }
        }

        /*
         * Implements IFacadeService
         */

        bool IServerFacade.Connect()
        {
            if (!_isOnline)
            {
                _isOnline = true;
                StartServices();
                Log.Show(_username, "Server is connected.");
                return true;
            }

            return false;
        }

        bool IServerFacade.Disconnect()
        { 
            if (_isOnline)
            {
                _isOnline = false;
                //StopServices(); TODO: since facade and lookup are implemented by this same object, stopping it will probably stop both services
                Log.Show(_username, "Server is disconnected.");
                return true;
            }

            return false;
        }




        void ILookupService.RegisterUser(string username, string ip, int port)
        {
            ClientMetadata client = new ClientMetadata();
            client.IP_Addr = ip;
            client.Port = port;
            client.Username = username;
            action.StoreClientInfo(client);  //First write to self
            RegisterInfoOnOtherServer(client);    
            Log.Show(_username, "Registered client " + username + ": " + ip + ":" + port);
        }


        private bool RegisterInfoOnOtherServer(ClientMetadata client)
        {

            IConsistencyService[] server = new IConsistencyService[_servers.Count];
            for (int i = 0; i < _servers.Count; i++)
            {
                server[i] = getOtherServers(_servers[i]);
            }

            callback returnedValueOnRegister1 = new callback();
            callback.RemoteAsyncDelegate RemoteDelforRegister1 = new callback.RemoteAsyncDelegate(() => server[0].WriteClientMetadata(client));
            AsyncCallback RemoteCallbackOnRegister1 = new AsyncCallback(returnedValueOnRegister1.OurRemoteAsyncCallBack);
            IAsyncResult RemArForRegister1 = RemoteDelforRegister1.BeginInvoke(RemoteCallbackOnRegister1, null);

            callback returnedValueOnRegister2 = new callback();
            callback.RemoteAsyncDelegate RemoteDelforRegister2 = new callback.RemoteAsyncDelegate(() => server[1].WriteClientMetadata(client));
            AsyncCallback RemoteCallbackOnRegister2 = new AsyncCallback(returnedValueOnRegister2.OurRemoteAsyncCallBack);
            IAsyncResult RemArForRegister2 = RemoteDelforRegister2.BeginInvoke(RemoteCallbackOnRegister2, null);

            Log.Show(_username, "[REGISTER CLIENT] Waiting for atleast one Server to return");

            returnedValueOnRegister1.waiter.WaitOne();

            if (returnedValueOnRegister1._status == false)
            {
                Log.Show(_username, "[REGISTER CLIENT] One of the Servers failed to register");
                returnedValueOnRegister2.waiter.WaitOne();
                if (returnedValueOnRegister2._status == false)
                {
                    Log.Show(_username, "[REGISTER CLIENT] Both the Servers failed to register");
                    return false;
                }
                else
                {
                    Log.Show(_username, "[REGISTER CLIENT] One Server successfully registered");
                    return true;
                }

            }
            else
            {
                Log.Show(_username, "[REGISTER CLIENT] One Server successfully registered");
                return true;
            }
        }


        void ILookupService.UnregisterUser(string username)  //Yet to implement
        {
            Log.Show(_username, "Unregistered client: " + username);

           // _clients.Remove(username);
        }

        ClientMetadata ILookupService.Lookup(string username)
        {
           
            return (lookUpOnOtherServers(username));
        }


        private ClientMetadata lookUpOnOtherServers(string username)
        {
            IConsistencyService[] server = new IConsistencyService[_servers.Count];
            for (int i = 0; i < _servers.Count; i++)
            {
                server[i] = getOtherServers(_servers[i]);
            }

            callback returnedValueOnLookup1 = new callback();
            callback.RemoteLookupDelegate RemoteDelforLookup1 = new callback.RemoteLookupDelegate(() => server[0].ReadClientMetadata(username));
            AsyncCallback RemoteCallbackOnLookup1 = new AsyncCallback(returnedValueOnLookup1.OurRemoteAsyncCallBack);
            IAsyncResult RemArForLookup1 = RemoteDelforLookup1.BeginInvoke(RemoteCallbackOnLookup1, null);

            callback returnedValueOnLookup2 = new callback();
            callback.RemoteLookupDelegate RemoteDelforLookup2 = new callback.RemoteLookupDelegate(() => server[1].ReadClientMetadata(username ));
            AsyncCallback RemoteCallbackOnLookup2 = new AsyncCallback(returnedValueOnLookup2.OurRemoteAsyncCallBack);
            IAsyncResult RemArForLookup2 = RemoteDelforLookup2.BeginInvoke(RemoteCallbackOnLookup2, null);

            Log.Show(_username, "[READ METADATA] Waiting for one server to return");
            returnedValueOnLookup1.waiter.WaitOne();

            //Compare the received value
            bool dataEqual;
            ClientMetadata myData = action.ReadClientMetadata(username);
            dataEqual = CompareValues(myData, returnedValueOnLookup1.data);

            if (dataEqual)
            {
                Log.Show(_username, "[READ METADATA] First retreived value matches");
                return myData;
            }

            else
            {
                returnedValueOnLookup2.waiter.WaitOne();
                dataEqual = CompareValues(myData, returnedValueOnLookup2.data);
                if (dataEqual)
                {
                    Log.Show(_username, "[READ METADATA] Second retreived value matches");
                    return myData;
                }
                else
                {
                    dataEqual = CompareValues(returnedValueOnLookup1.data, returnedValueOnLookup2.data);
                    if (dataEqual)
                    {
                        Log.Show(_username, "[READ METADATA] I have an outdated value. Other two fetched values match");
                        action.StoreClientInfo(returnedValueOnLookup1.data);
                        Log.Show(_username, "[READ METADATA] Updated my value to one of the received values");
                        return returnedValueOnLookup1.data;
                    }
                    else
                    {
                        Log.Show(_username, "[READ METADATA]ERROR - ERROR");
                        return null;
                    }
                }

            }
       }

        private bool CompareValues(ClientMetadata myValue, ClientMetadata receivedValue)
        {
            if (myValue.Username == receivedValue.Username && myValue.IP_Addr == receivedValue.IP_Addr && myValue.Port == receivedValue.Port)
                return true;
            else
                return false;
        }



        int ILookupService.NextSequenceNumber()
        {
            _sequenceNumber++; //increment sequence number and then try to acquire.
            WriteSequenceNumberOnOtherServers();  //to test client functionality, comment this line to generate sequence number from one server.
            Log.Show(_username, "[SEQ NUMBER]Sequence number retrieved. Next sequence number is: " + (_sequenceNumber));
            return _sequenceNumber;

        }

        private void WriteSequenceNumberOnOtherServers()
        {
             IConsistencyService[] server = new IConsistencyService[_servers.Count];
            for (int i = 0; i < _servers.Count; i++)
            {
                server[i] = getOtherServers(_servers[i]);
            }

            //Write  generated sequence number to ourself.
            bool status = action.WriteSequenceNumber(_sequenceNumber);
            Log.Show(_username, "[SEQ NUMBER] Generated sequence number write to ourself returned: " + status);
            if (status == false)
            {
                _sequenceNumber++;
                WriteSequenceNumberOnOtherServers();
            }

            callback returnedValue1 = new callback();
		   callback.RemoteAsyncDelegate RemoteDel1 = new callback.RemoteAsyncDelegate(()=> server[0].WriteSequenceNumber(_sequenceNumber) );
		    AsyncCallback RemoteCallback1 = new AsyncCallback(returnedValue1.OurRemoteAsyncCallBack);
            IAsyncResult RemAr1 = RemoteDel1.BeginInvoke(RemoteCallback1, null);
         

            callback returnedValue2 = new callback();
            callback.RemoteAsyncDelegate RemoteDel2 = new callback.RemoteAsyncDelegate(() => server[1].WriteSequenceNumber(_sequenceNumber));
            AsyncCallback RemoteCallback2 = new AsyncCallback(returnedValue2.OurRemoteAsyncCallBack);
            IAsyncResult RemAr2 = RemoteDel2.BeginInvoke(RemoteCallback2, null);


            Log.Show(_username, "[SEQ NUMBER] Waiting for atleast one Server to return");

            returnedValue1.waiter.WaitOne();

            if (returnedValue1._status == false)
            {
                Log.Show(_username, "[SEQ NUMBER] One of the Servers failed to set the sequence number. " + returnedValue1._status);
                returnedValue2.waiter.WaitOne();

                if (returnedValue2._status == false)
                {
                    Log.Show(_username, "[SEQ NUMBER] Both servers failed to set the sequence number ");
                    _sequenceNumber++;
                    WriteSequenceNumberOnOtherServers(); // try until you get a sequence number.
                }

                else
                {
                    Log.Show(_username, "[SEQ NUMBER] One server successfully set the sequence number. " + returnedValue2._status);
                }

            }

            else
            {
                Log.Show(_username, "[SEQ NUMBER] One of the servers successfully set the sequence number. Return!!");
            }
        }


        private IConsistencyService getOtherServers(ServerMetadata servers)
        {
                ServerMetadata chosenServer = servers;
                String connectionString = "tcp://" + chosenServer.IP_Addr + ":" + chosenServer.Port + "/" + servers.Username + "/" + Common.Constants.CONSISTENCY_SERVICE_NAME;
                Log.Show(_username, "Trying to find server: " + connectionString);

                IConsistencyService server = (IConsistencyService)Activator.GetObject(
                    typeof(IConsistencyService),
                    connectionString);

                return server;
       
        }
    }
}