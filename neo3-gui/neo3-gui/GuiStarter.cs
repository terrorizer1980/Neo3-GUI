﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.Configuration;
using Neo.Common;
using Neo.Common.Analyzers;
using Neo.Common.Consoles;
using Neo.Common.Scanners;
using Neo.Common.Utility;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;

namespace Neo
{
    public class GuiStarter : MainService
    {
        public GuiStarter()
        {
            ExecuteResultLogTracker = new ExecuteResultLogTracker();
            ExecuteResultScanner = new ExecuteResultScanner();
        }

   
        public override async Task Start(string[] args)
        {
            await base.Start(args);
            Task.Run(() => ExecuteResultScanner.Start());
            UnconfirmedTransactionCache.RegisterBlockPersistEvent(this.NeoSystem);
        }

        public readonly ExecuteResultScanner ExecuteResultScanner;

        public readonly ExecuteResultLogTracker ExecuteResultLogTracker;


    }
}
