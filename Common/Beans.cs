﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Common.Beans
{

    public class ReservationRequest
    {

        public string Description
        {
            get;
            set;
        }

        public string[] Users
        {
            get;
            set;
        }

        public int[] Slots
        {
            get;
            set;
        }

    }


    public class ClientMetadata
    {

        public string Username
        {
            get;
            set;
        }

        public string IP_Addr
        {
            get;
            set;
        }

        public int Port
        {
            get;
            set;
        }

    }

    public class ServerMetadata
    {

        public string Username
        {
            get;
            set;
        }

        public string IP_Addr
        {
            get;
            set;
        }

        public int Port
        {
            get;
            set;
        }

    }

}
