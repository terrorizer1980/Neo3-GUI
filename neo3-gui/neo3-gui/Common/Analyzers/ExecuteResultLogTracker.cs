﻿using System;
using System.Collections.Generic;
using System.Linq;
using Neo.Common.Storage;
using Neo.Common.Storage.LevelDBModules;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;

namespace Neo.Common.Analyzers
{

    public class ExecuteResultLogTracker : Plugin, IPersistencePlugin
    {
        private readonly LevelDbContext _levelDb = new LevelDbContext();

        private readonly HashSet<UInt160> _cachedAssets = new HashSet<UInt160>();

        void IPersistencePlugin.OnPersist(NeoSystem system, Block block, DataCache snapshot, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
        {
            Header header = snapshot.GetCurrentHeader();
            var analyzer = new BlockAnalyzer(snapshot, header, applicationExecutedList);
            analyzer.Analysis();

            foreach (var analyzerResultInfo in analyzer.Result.ExecuteResultInfos)
            {
                _levelDb.SaveTxExecuteLog(analyzerResultInfo);
            }
            foreach (var analyzerAssetInfo in analyzer.Result.AssetInfos)
            {
                if (!_cachedAssets.Contains(analyzerAssetInfo.Key))
                {
                    _levelDb.SaveAssetInfo(analyzerAssetInfo.Value);
                    _cachedAssets.Add(analyzerAssetInfo.Key);
                }
            }

            if (analyzer.Result.Transfers.NotEmpty())
            {
                _levelDb.SaveTransfers(snapshot.GetHeight(), analyzer.Result.Transfers);
            }

            if (analyzer.Result.BalanceChangeAccounts.NotEmpty())
            {
                _levelDb.UpdateBalancingAccounts(snapshot.GetHeight(), analyzer.Result.BalanceChangeAccounts);
                foreach (var item in analyzer.Result.BalanceChangeAccounts)
                {
                    var balance = item.Account.GetBalanceOf(item.Asset, snapshot);
                    _levelDb.UpdateBalance(item.Account, item.Asset, balance.Value, snapshot.GetHeight());
                }
            }

            if (analyzer.Result.ContractChangeEvents.NotEmpty())
            {
                _levelDb.SaveContractEvent(snapshot.GetHeight(), analyzer.Result.ContractChangeEvents);

            }
        }


        void IPersistencePlugin.OnCommit(NeoSystem system, Block block, DataCache snapshot)
        {
            _levelDb.Commit();
        }

    }
}
