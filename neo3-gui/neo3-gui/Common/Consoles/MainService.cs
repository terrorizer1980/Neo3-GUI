using Akka.Actor;
using Microsoft.Extensions.Configuration;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Capabilities;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.Services;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets;
using Neo.Wallets.NEP6;
using Neo.Wallets.SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ECCurve = Neo.Cryptography.ECC.ECCurve;
using ECPoint = Neo.Cryptography.ECC.ECPoint;

namespace Neo.Common.Consoles
{
    public class MainService : ConsoleServiceBase
    {
        public event EventHandler WalletChanged;

        public LocalNode LocalNode;

        private Wallet currentWallet;
        public Wallet CurrentWallet
        {
            get
            {
                return currentWallet;
            }
            internal set
            {
                currentWallet = value;
                WalletChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private NeoSystem neoSystem;
        public NeoSystem NeoSystem
        {
            get
            {
                return neoSystem;
            }
            private set
            {
                neoSystem = value;
            }
        }

        protected override string Prompt => "neo";
        public override string ServiceName => "NEO-CLI";

        public virtual async Task Start(string[] args)
        {
            if (NeoSystem != null) return;
            try
            {
                NeoSystem = new NeoSystem(CliSettings.Default.Protocol, CliSettings.Default.Storage.Engine, CliSettings.Default.Storage.Path);
                NeoSystem.AddService(this);

                LocalNode = await NeoSystem.LocalNode.Ask<LocalNode>(new LocalNode.GetInstance());
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            using (IEnumerator<Block> blocksBeingImported = GetBlocksFromFile().GetEnumerator())
            {
                while (true)
                {
                    List<Block> blocksToImport = new List<Block>();
                    for (int i = 0; i < 10; i++)
                    {
                        if (!blocksBeingImported.MoveNext()) break;
                        blocksToImport.Add(blocksBeingImported.Current);
                    }
                    if (blocksToImport.Count == 0) break;
                    await NeoSystem.Blockchain.Ask<Blockchain.ImportCompleted>(new Blockchain.Import { Blocks = blocksToImport });
                    if (NeoSystem is null) return;
                }
            }
            NeoSystem.StartNode(new ChannelsConfig
            {
                Tcp = new IPEndPoint(IPAddress.Any, CliSettings.Default.P2P.Port),
                WebSocket = new IPEndPoint(IPAddress.Any, CliSettings.Default.P2P.WsPort),
                MinDesiredConnections = CliSettings.Default.P2P.MinDesiredConnections,
                MaxConnections = CliSettings.Default.P2P.MaxConnections,
                MaxConnectionsPerAddress = CliSettings.Default.P2P.MaxConnectionsPerAddress
            });
            if (CliSettings.Default.UnlockWallet.IsActive)
            {
                try
                {
                    OpenWallet(CliSettings.Default.UnlockWallet.Path, CliSettings.Default.UnlockWallet.Password);
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine($"Warning: wallet file \"{CliSettings.Default.UnlockWallet.Path}\" not found.");
                }
                catch (CryptographicException)
                {
                    Console.WriteLine($"failed to open file \"{CliSettings.Default.UnlockWallet.Path}\"");
                }

            }
        }

        public void Stop()
        {
            Interlocked.Exchange(ref neoSystem, null)?.Dispose();
        }



        public void CreateWallet(string path, string password)
        {
            switch (Path.GetExtension(path))
            {
                case ".db3":
                    {
                        UserWallet wallet = UserWallet.Create(path, password, CliSettings.Default.Protocol);
                        WalletAccount account = wallet.CreateAccount();
                        Console.WriteLine($"address: {account.Address}");
                        Console.WriteLine($" pubkey: {account.GetKey().PublicKey.EncodePoint(true).ToHexString()}");
                        CurrentWallet = wallet;
                    }
                    break;
                case ".json":
                    {
                        NEP6Wallet wallet = new NEP6Wallet(path, CliSettings.Default.Protocol);
                        wallet.Unlock(password);
                        WalletAccount account = wallet.CreateAccount();
                        wallet.Save();
                        Console.WriteLine($"address: {account.Address}");
                        Console.WriteLine($" pubkey: {account.GetKey().PublicKey.EncodePoint(true).ToHexString()}");
                        CurrentWallet = wallet;
                    }
                    break;
                default:
                    Console.WriteLine("Wallet files in that format are not supported, please use a .json or .db3 file extension.");
                    break;
            }
        }

        private IEnumerable<Block> GetBlocks(Stream stream, bool read_start = false)
        {
            using BinaryReader r = new BinaryReader(stream);
            uint start = read_start ? r.ReadUInt32() : 0;
            uint count = r.ReadUInt32();
            uint end = start + count - 1;
            uint currentHeight = NativeContract.Ledger.CurrentIndex(NeoSystem.StoreView);
            if (end <= currentHeight) yield break;
            for (uint height = start; height <= end; height++)
            {
                var size = r.ReadInt32();
                if (size > Message.PayloadMaxSize)
                    throw new ArgumentException($"Block {height} exceeds the maximum allowed size");

                byte[] array = r.ReadBytes(size);
                if (height > currentHeight)
                {
                    Block block = array.AsSerializable<Block>();
                    yield return block;
                }
            }
        }

        private IEnumerable<Block> GetBlocksFromFile()
        {
            const string pathAcc = "chain.acc";
            if (File.Exists(pathAcc))
                using (FileStream fs = new FileStream(pathAcc, FileMode.Open, FileAccess.Read, FileShare.Read))
                    foreach (var block in GetBlocks(fs))
                        yield return block;

            const string pathAccZip = pathAcc + ".zip";
            if (File.Exists(pathAccZip))
                using (FileStream fs = new FileStream(pathAccZip, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                using (Stream zs = zip.GetEntry(pathAcc).Open())
                    foreach (var block in GetBlocks(zs))
                        yield return block;

            var paths = Directory.EnumerateFiles(".", "chain.*.acc", SearchOption.TopDirectoryOnly).Concat(Directory.EnumerateFiles(".", "chain.*.acc.zip", SearchOption.TopDirectoryOnly)).Select(p => new
            {
                FileName = Path.GetFileName(p),
                Start = uint.Parse(Regex.Match(p, @"\d+").Value),
                IsCompressed = p.EndsWith(".zip")
            }).OrderBy(p => p.Start);

            uint height = NativeContract.Ledger.CurrentIndex(NeoSystem.StoreView);
            foreach (var path in paths)
            {
                if (path.Start > height + 1) break;
                if (path.IsCompressed)
                    using (FileStream fs = new FileStream(path.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                    using (Stream zs = zip.GetEntry(Path.GetFileNameWithoutExtension(path.FileName)).Open())
                        foreach (var block in GetBlocks(zs, true))
                            yield return block;
                else
                    using (FileStream fs = new FileStream(path.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                        foreach (var block in GetBlocks(fs, true))
                            yield return block;
            }
        }

        private bool NoWallet()
        {
            if (CurrentWallet != null) return false;
            Console.WriteLine("You have to open the wallet first.");
            return true;
        }

        protected override bool OnCommand(string[] args)
        {
            if (Plugin.SendMessage(args)) return true;
            switch (args[0].ToLower())
            {
                case "sign":
                    return OnSignCommand(args);
                case "change":
                    return OnChangeCommand(args);
                case "create":
                    return OnCreateCommand(args);
                case "export":
                    return OnExportCommand(args);
                case "help":
                    return OnHelpCommand(args);
                case "plugins":
                    return OnPluginsCommand(args);
                case "import":
                    return OnImportCommand(args);
                case "list":
                    return OnListCommand(args);
                case "open":
                    return OnOpenCommand(args);
                case "close":
                    return OnCloseCommand(args);
                case "send":
                    return OnSendCommand(args);
                case "show":
                    return OnShowCommand(args);
                case "start":
                    return OnStartCommand(args);
                case "upgrade":
                    return OnUpgradeCommand(args);

                case "invoke":
                    return OnInvokeCommand(args);
                case "install":
                    return OnInstallCommand(args);
                case "uninstall":
                    return OnUnInstallCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }



        private bool OnInvokeCommand(string[] args)
        {
            var scriptHash = UInt160.Parse(args[1]);

            List<ContractParameter> contractParameters = new List<ContractParameter>();
            for (int i = 3; i < args.Length; i++)
            {
                contractParameters.Add(new ContractParameter()
                {
                    // TODO: support contract params of type other than string.
                    Type = ContractParameterType.String,
                    Value = args[i]
                });
            }

            Transaction tx = new Transaction
            {
                Signers = new Signer[] { new Signer() { Account = UInt160.Zero } },
                Attributes = new TransactionAttribute[0],
                Witnesses = new Witness[0]
            };

            using (ScriptBuilder scriptBuilder = new ScriptBuilder())
            {
                scriptBuilder.EmitDynamicCall(scriptHash, args[2], contractParameters.ToArray());
                tx.Script = scriptBuilder.ToArray();
                Console.WriteLine($"Invoking script with: '{tx.Script.ToHexString()}'");
            }

            using (ApplicationEngine engine = tx.Script.RunTestMode(null, tx))
            {
                Console.WriteLine($"VM State: {engine.State}");
                Console.WriteLine($"Gas Consumed: {engine.GasConsumed}");
                Console.WriteLine($"Evaluation Stack: {new JArray(engine.ResultStack.Select(p => p.ToContractParameter().ToJson()))}");
                Console.WriteLine();
                if (engine.State.HasFlag(VMState.FAULT))
                {
                    Console.WriteLine("Engine faulted.");
                    return true;
                }
            }

            if (NoWallet()) return true;
            try
            {
                tx = CurrentWallet.MakeTransaction(NeoSystem.StoreView, tx.Script);
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Error: insufficient balance.");
                return true;
            }
            if (!ReadUserInput("relay tx(no|yes)").IsYes())
            {
                return true;
            }
            return SignAndSendTx(tx);
        }


        private bool SignAndSendTx(Transaction tx)
        {
            ContractParametersContext context;
            try
            {
                context = new ContractParametersContext(NeoSystem.StoreView, tx, CliSettings.Default.Protocol.Network);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Error creating contract params: {ex}");
                throw;
            }
            CurrentWallet.Sign(context);
            string msg;
            if (context.Completed)
            {
                tx.Witnesses = context.GetWitnesses();

                NeoSystem.Blockchain.Tell(tx);
                //NeoSystem.LocalNode.Tell(new LocalNode.Relay { Inventory = tx });

                msg = $"Signed and relayed transaction with hash={tx.Hash}";
                Console.WriteLine(msg);
                return true;
            }

            msg = $"Failed sending transaction with hash={tx.Hash}";
            Console.WriteLine(msg);
            return true;
        }



        private bool OnSignCommand(string[] args)
        {
            if (NoWallet()) return true;

            if (args.Length < 2)
            {
                Console.WriteLine("You must input JSON object pending signature data.");
                return true;
            }
            var jsonObjectToSign = string.Join(string.Empty, args.Skip(1));
            if (string.IsNullOrWhiteSpace(jsonObjectToSign))
            {
                Console.WriteLine("You must input JSON object pending signature data.");
                return true;
            }
            try
            {
                ContractParametersContext context = ContractParametersContext.Parse(jsonObjectToSign, NeoSystem.StoreView);
                if (!CurrentWallet.Sign(context))
                {
                    Console.WriteLine("The private key that can sign the data is not found.");
                    return true;
                }
                Console.WriteLine($"Signed Output:{Environment.NewLine}{context}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"One or more errors occurred:{Environment.NewLine}{e.Message}");
            }
            return true;
        }

        private bool OnChangeCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "view":
                    return OnChangeViewCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnChangeViewCommand(string[] args)
        {
            if (args.Length != 3) return false;
            if (!byte.TryParse(args[2], out byte viewnumber)) return false;
            return true;
        }

        private bool OnCreateCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "address":
                    return OnCreateAddressCommand(args);
                case "wallet":
                    return OnCreateWalletCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnCreateAddressCommand(string[] args)
        {
            if (NoWallet()) return true;
            if (args.Length > 3)
            {
                Console.WriteLine("error");
                return true;
            }

            string path = "address.txt";
            if (File.Exists(path))
            {
                if (!ReadUserInput($"The file '{path}' already exists, do you want to overwrite it? (yes|no)", false).IsYes())
                {
                    return true;
                }
            }

            ushort count;
            if (args.Length >= 3)
                count = ushort.Parse(args[2]);
            else
                count = 1;

            List<string> addresses = new List<string>();
            using (var percent = new ConsolePercent(0, count))
            {
                Parallel.For(0, count, (i) =>
                {
                    WalletAccount account = CurrentWallet.CreateAccount();
                    addresses.Add(account.Address);
                    lock (addresses)
                    {
                        addresses.Add(account.Address);
                        percent.Value++;
                    }
                });
            }

            if (CurrentWallet is NEP6Wallet wallet)
                wallet.Save();

            Console.WriteLine($"export addresses to {path}");
            File.WriteAllLines(path, addresses);
            return true;
        }

        private bool OnCreateWalletCommand(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("error");
                return true;
            }
            string path = args[2];
            string password = ReadUserInput("password", true);
            if (password.Length == 0)
            {
                Console.WriteLine("cancelled");
                return true;
            }
            string password2 = ReadUserInput("password", true);
            if (password != password2)
            {
                Console.WriteLine("error");
                return true;
            }
            CreateWallet(path, password);
            return true;
        }

        private bool OnExportCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "block":
                case "blocks":
                    return OnExportBlocksCommand(args);
                case "key":
                    return OnExportKeyCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnExportBlocksCommand(string[] args)
        {
            uint height = NativeContract.Ledger.CurrentIndex(NeoSystem.StoreView);
            var start = args.Length > 2 ? uint.Parse(args[2]) : 0;
            var count = args.Length > 3 ? uint.Parse(args[3]) : uint.MaxValue;
            var path = args.Length > 4 ? args[4] : "chain.acc";

            if (height < start)
            {
                Console.WriteLine("Error: invalid start height.");
                return true;
            }

            count = Math.Min(count, height - start + 1);

            if (start > 0)
            {
                path = $"chain.{start}.acc";
            }
            WriteBlocks(start, count, path, true);
            return true;
        }



        private bool OnExportKeyCommand(string[] args)
        {
            if (NoWallet()) return true;
            if (args.Length < 2 || args.Length > 4)
            {
                Console.WriteLine("error");
                return true;
            }
            UInt160 scriptHash = null;
            string path = null;
            if (args.Length == 3)
            {
                try
                {
                    scriptHash = args[2].ToScriptHash();
                }
                catch (FormatException)
                {
                    path = args[2];
                }
            }
            else if (args.Length == 4)
            {
                scriptHash = args[2].ToScriptHash();
                path = args[3];
            }
            if (File.Exists(path))
            {
                Console.WriteLine($"Error: File '{path}' already exists");
                return true;
            }
            string password = ReadUserInput("password", true);
            if (password.Length == 0)
            {
                Console.WriteLine("cancelled");
                return true;
            }
            if (!CurrentWallet.VerifyPassword(password))
            {
                Console.WriteLine("Incorrect password");
                return true;
            }
            IEnumerable<KeyPair> keys;
            if (scriptHash == null)
                keys = CurrentWallet.GetAccounts().Where(p => p.HasKey).Select(p => p.GetKey());
            else
                keys = new[] { CurrentWallet.GetAccount(scriptHash).GetKey() };
            if (path == null)
                foreach (KeyPair key in keys)
                    Console.WriteLine(key.Export());
            else
                File.WriteAllLines(path, keys.Select(p => p.Export()));
            return true;
        }

        private bool OnHelpCommand(string[] args)
        {
            Console.WriteLine("Normal Commands:");
            Console.WriteLine("\tversion");
            Console.WriteLine("\thelp [plugin-name]");
            Console.WriteLine("\tclear");
            Console.WriteLine("\texit");
            Console.WriteLine("Wallet Commands:");
            Console.WriteLine("\tcreate wallet <path>");
            Console.WriteLine("\topen wallet <path>");
            Console.WriteLine("\tclose wallet");
            Console.WriteLine("\tupgrade wallet <path>");
            Console.WriteLine("\tlist address");
            Console.WriteLine("\tlist asset");
            Console.WriteLine("\tlist key");
            Console.WriteLine("\tshow gas");
            Console.WriteLine("\tcreate address [n=1]");
            Console.WriteLine("\timport key <wif|path>");
            Console.WriteLine("\texport key [address] [path]");
            Console.WriteLine("\timport multisigaddress m pubkeys...");
            Console.WriteLine("\tsend <id|alias> <address> <value>");
            Console.WriteLine("\tsign <jsonObjectToSign>");
            Console.WriteLine("Contract Commands:");
            Console.WriteLine("\tdeploy <nefFilePath> [manifestFile]");
            Console.WriteLine("\tinvoke <scripthash> <command> [optionally quoted params separated by space]");
            Console.WriteLine("Node Commands:");
            Console.WriteLine("\tshow state");
            Console.WriteLine("\tshow pool [verbose]");
            Console.WriteLine("\trelay <jsonObjectToSign>");
            Console.WriteLine("Plugin Commands:");
            Console.WriteLine("\tplugins");
            Console.WriteLine("\tinstall <pluginName>");
            Console.WriteLine("\tuninstall <pluginName>");
            Console.WriteLine("Advanced Commands:");
            Console.WriteLine("\texport blocks <index>");
            Console.WriteLine("\tstart consensus");
            return true;
        }

        private bool OnPluginsCommand(string[] args)
        {
            if (Plugin.Plugins.Count > 0)
            {
                Console.WriteLine("Loaded plugins:");
                Plugin.Plugins.ForEach(p => Console.WriteLine("\t" + p.Name));
            }
            else
            {
                Console.WriteLine("No loaded plugins");
            }
            return true;
        }

        private bool OnImportCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "key":
                    return OnImportKeyCommand(args);
                case "multisigaddress":
                    return OnImportMultisigAddress(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnImportMultisigAddress(string[] args)
        {
            if (NoWallet()) return true;

            if (args.Length < 4)
            {
                Console.WriteLine("Error. Invalid parameters.");
                return true;
            }

            int m = int.Parse(args[2]);
            int n = args.Length - 3;

            if (m < 1 || m > n || n > 1024)
            {
                Console.WriteLine("Error. Invalid parameters.");
                return true;
            }

            ECPoint[] publicKeys = args.Skip(3).Select(p => ECPoint.Parse(p, ECCurve.Secp256r1)).ToArray();

            Contract multiSignContract = Contract.CreateMultiSigContract(m, publicKeys);
            KeyPair keyPair = CurrentWallet.GetAccounts().FirstOrDefault(p => p.HasKey && publicKeys.Contains(p.GetKey().PublicKey))?.GetKey();

            WalletAccount account = CurrentWallet.CreateAccount(multiSignContract, keyPair);
            if (CurrentWallet is NEP6Wallet wallet)
                wallet.Save();

            Console.WriteLine("Multisig. Addr.: " + multiSignContract.ScriptHash.ToAddress());

            return true;
        }

        private bool OnImportKeyCommand(string[] args)
        {
            if (args.Length > 3)
            {
                Console.WriteLine("error");
                return true;
            }
            byte[] prikey = null;
            try
            {
                prikey = Wallet.GetPrivateKeyFromWIF(args[2]);
            }
            catch (FormatException) { }
            if (prikey == null)
            {
                var file = new FileInfo(args[2]);

                if (!file.Exists)
                {
                    Console.WriteLine($"Error: File '{file.FullName}' doesn't exists");
                    return true;
                }

                if (file.Length > 1024 * 1024)
                {
                    if (!ReadUserInput($"The file '{file.FullName}' is too big, do you want to continue? (yes|no)", false).IsYes())
                    {
                        return true;
                    }
                }

                string[] lines = File.ReadAllLines(args[2]).Where(u => !string.IsNullOrEmpty(u)).ToArray();
                using (var percent = new ConsolePercent(0, lines.Length))
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Length == 64)
                            prikey = lines[i].HexToBytes();
                        else
                            prikey = Wallet.GetPrivateKeyFromWIF(lines[i]);
                        CurrentWallet.CreateAccount(prikey);
                        Array.Clear(prikey, 0, prikey.Length);
                        percent.Value++;
                    }
                }
            }
            else
            {
                WalletAccount account = CurrentWallet.CreateAccount(prikey);
                Array.Clear(prikey, 0, prikey.Length);
                Console.WriteLine($"address: {account.Address}");
                Console.WriteLine($" pubkey: {account.GetKey().PublicKey.EncodePoint(true).ToHexString()}");
            }
            if (CurrentWallet is NEP6Wallet wallet)
                wallet.Save();
            return true;
        }

        private bool OnListCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "address":
                    return OnListAddressCommand(args);
                case "asset":
                    return OnListAssetCommand(args);
                case "key":
                    return OnListKeyCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnShowGasCommand(string[] args)
        {
            if (NoWallet()) return true;
            BigInteger gas = BigInteger.Zero;
            var snapshot = NeoSystem.StoreView;
            uint height = NativeContract.Ledger.CurrentIndex(snapshot) + 1;
            foreach (UInt160 account in CurrentWallet.GetAccounts().Select(p => p.ScriptHash))
                gas += NativeContract.NEO.UnclaimedGas(snapshot, account, height);
            Console.WriteLine($"Unclaimed gas: {new BigDecimal(gas, NativeContract.GAS.Decimals)}");
            return true;
        }

        private bool OnListKeyCommand(string[] args)
        {
            if (NoWallet()) return true;
            foreach (KeyPair key in CurrentWallet.GetAccounts().Where(p => p.HasKey).Select(p => p.GetKey()))
            {
                Console.WriteLine(key.PublicKey);
            }
            return true;
        }

        private bool OnListAddressCommand(string[] args)
        {
            if (NoWallet()) return true;

            var snapshot = NeoSystem.StoreView;
            foreach (var account in CurrentWallet.GetAccounts())
            {
                var contract = account.Contract;
                var type = "Nonstandard";

                if (account.WatchOnly)
                {
                    type = "WatchOnly";
                }
                else if (contract.Script.IsMultiSigContract())
                {
                    type = "MultiSignature";
                }
                else if (contract.Script.IsSignatureContract())
                {
                    type = "Standard";
                }
                else if (NativeContract.ContractManagement.GetContract(snapshot, account.ScriptHash) != null)
                {
                    type = "Deployed-Nonstandard";
                }

                Console.WriteLine($"{"   Address: "}{account.Address}\t{type}");
                Console.WriteLine($"{"ScriptHash: "}{account.ScriptHash}\n");
            }

            return true;
        }

        private bool OnListAssetCommand(string[] args)
        {
            if (NoWallet()) return true;
            var snapshot = NeoSystem.StoreView;
            foreach (UInt160 account in CurrentWallet.GetAccounts().Select(p => p.ScriptHash))
            {
                Console.WriteLine(account.ToAddress(NeoSystem.Settings.AddressVersion));
                Console.WriteLine($"NEO: {CurrentWallet.GetBalance(snapshot, NativeContract.NEO.Hash, account)}");
                Console.WriteLine($"GAS: {CurrentWallet.GetBalance(snapshot, NativeContract.GAS.Hash, account)}");
                Console.WriteLine();
            }
            Console.WriteLine("----------------------------------------------------");
            Console.WriteLine($"Total:   NEO: {CurrentWallet.GetAvailable(snapshot, NativeContract.NEO.Hash),10}     GAS: {CurrentWallet.GetAvailable(snapshot, NativeContract.GAS.Hash),18}");
            Console.WriteLine();
            Console.WriteLine("NEO hash: " + NativeContract.NEO.Hash);
            Console.WriteLine("GAS hash: " + NativeContract.GAS.Hash);
            return true;
        }

        private bool OnOpenCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "wallet":
                    return OnOpenWalletCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        //TODO: 目前没有想到其它安全的方法来保存密码
        //所以只能暂时手动输入，但如此一来就不能以服务的方式启动了
        //未来再想想其它办法，比如采用智能卡之类的
        private bool OnOpenWalletCommand(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("error");
                return true;
            }
            string path = args[2];
            if (!File.Exists(path))
            {
                Console.WriteLine($"File does not exist");
                return true;
            }
            string password = ReadUserInput("password", true);
            if (password.Length == 0)
            {
                Console.WriteLine("cancelled");
                return true;
            }
            try
            {
                OpenWallet(path, password);
            }
            catch (CryptographicException)
            {
                Console.WriteLine($"failed to open file \"{path}\"");
            }
            return true;
        }

        /// <summary>
        /// process "close" command
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private bool OnCloseCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "wallet":
                    return OnCloseWalletCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        /// <summary>
        /// process "close wallet" command
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public bool OnCloseWalletCommand(string[] args)
        {
            if (CurrentWallet == null)
            {
                Console.WriteLine($"Wallet is not opened");
                return true;
            }
            CurrentWallet = null;
            Console.WriteLine($"Wallet is closed");
            return true;
        }

        private bool OnSendCommand(string[] args)
        {
            if (args.Length != 4)
            {
                Console.WriteLine("error");
                return true;
            }
            if (NoWallet()) return true;
            string password = ReadUserInput("password", true);
            if (password.Length == 0)
            {
                Console.WriteLine("cancelled");
                return true;
            }
            if (!CurrentWallet.VerifyPassword(password))
            {
                Console.WriteLine("Incorrect password");
                return true;
            }
            UInt160 assetId;
            switch (args[1].ToLower())
            {
                case "neo":
                    assetId = NativeContract.NEO.Hash;
                    break;
                case "gas":
                    assetId = NativeContract.GAS.Hash;
                    break;
                default:
                    assetId = UInt160.Parse(args[1]);
                    break;
            }
            UInt160 to = args[2].ToScriptHash();
            var snapshot = NeoSystem.StoreView;
            Transaction tx;
            AssetDescriptor descriptor = new AssetDescriptor(snapshot, CliSettings.Default.Protocol, assetId);
            if (!BigDecimal.TryParse(args[3], descriptor.Decimals, out BigDecimal amount) || amount.Sign <= 0)
            {
                Console.WriteLine("Incorrect Amount Format");
                return true;
            }
            tx = CurrentWallet.MakeTransaction(snapshot, new[]
            {
                new TransferOutput
                {
                    AssetId = assetId,
                    Value = amount,
                    ScriptHash = to
                }
            });

            if (tx == null)
            {
                Console.WriteLine("Insufficient funds");
                return true;
            }

            ContractParametersContext context = new ContractParametersContext(snapshot, tx, CliSettings.Default.Protocol.Network);
            CurrentWallet.Sign(context);
            if (context.Completed)
            {
                tx.Witnesses = context.GetWitnesses();
                NeoSystem.Blockchain.Tell(tx);
                //NeoSystem.LocalNode.Tell(new LocalNode.Relay { Inventory = tx });
                Console.WriteLine($"TXID: {tx.Hash}");
            }
            else
            {
                Console.WriteLine("SignatureContext:");
                Console.WriteLine(context.ToString());
            }

            return true;
        }

        private bool OnShowCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "gas":
                    return OnShowGasCommand(args);
                case "pool":
                    return OnShowPoolCommand(args);
                case "state":
                    return OnShowStateCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnShowPoolCommand(string[] args)
        {
            bool verbose = args.Length >= 3 && args[2] == "verbose";

            int verifiedCount, unverifiedCount;
            if (verbose)
            {
                NeoSystem.MemPool.GetVerifiedAndUnverifiedTransactions(
                    out IEnumerable<Transaction> verifiedTransactions,
                    out IEnumerable<Transaction> unverifiedTransactions);
                Console.WriteLine("Verified Transactions:");
                foreach (Transaction tx in verifiedTransactions)
                    Console.WriteLine($" {tx.Hash} {tx.GetType().Name} {tx.NetworkFee} GAS_NetFee");
                Console.WriteLine("Unverified Transactions:");
                foreach (Transaction tx in unverifiedTransactions)
                    Console.WriteLine($" {tx.Hash} {tx.GetType().Name} {tx.NetworkFee} GAS_NetFee");

                verifiedCount = verifiedTransactions.Count();
                unverifiedCount = unverifiedTransactions.Count();
            }
            else
            {
                verifiedCount = NeoSystem.MemPool.VerifiedCount;
                unverifiedCount = NeoSystem.MemPool.UnVerifiedCount;
            }
            Console.WriteLine($"total: {NeoSystem.MemPool.Count}, verified: {verifiedCount}, unverified: {unverifiedCount}");
            return true;
        }

        private bool OnShowStateCommand(string[] args)
        {
            var cancel = new CancellationTokenSource();

            Console.CursorVisible = false;
            Console.Clear();

            Task broadcast = Task.Run(async () =>
            {
                while (!cancel.Token.IsCancellationRequested)
                {
                    NeoSystem.LocalNode.Tell(Message.Create(MessageCommand.Ping, PingPayload.Create(NativeContract.Ledger.CurrentIndex(NeoSystem.StoreView))));
                    await Task.Delay(NeoSystem.Settings.TimePerBlock, cancel.Token);
                }
            });
            Task task = Task.Run(async () =>
            {
                int maxLines = 0;
                while (!cancel.Token.IsCancellationRequested)
                {
                    uint height = NativeContract.Ledger.CurrentIndex(NeoSystem.StoreView);
                    uint headerHeight = NeoSystem.HeaderCache.Last?.Index ?? height;

                    Console.SetCursorPosition(0, 0);
                    WriteLineWithoutFlicker($"block: {height}/{headerHeight}  connected: {LocalNode.ConnectedCount}  unconnected: {LocalNode.UnconnectedCount}", Console.WindowWidth - 1);

                    int linesWritten = 1;
                    foreach (RemoteNode node in LocalNode.GetRemoteNodes().OrderByDescending(u => u.LastBlockIndex).Take(Console.WindowHeight - 2).ToArray())
                    {
                        Console.WriteLine(
                            $"  ip: {node.Remote.Address,-15}\tport: {node.Remote.Port,-5}\tlisten: {node.ListenerTcpPort,-5}\theight: {node.LastBlockIndex,-7}");
                        linesWritten++;
                    }

                    maxLines = Math.Max(maxLines, linesWritten);

                    while (linesWritten < maxLines)
                    {
                        WriteLineWithoutFlicker("", Console.WindowWidth - 1);
                        maxLines--;
                    }

                    await Task.Delay(500, cancel.Token);
                }
            });
            ReadLine();
            cancel.Cancel();
            try { Task.WaitAll(task, broadcast); } catch { }
            Console.WriteLine();
            Console.CursorVisible = true;
            return true;
        }


        private bool OnStartCommand(string[] args)
        {
            switch (args[1].ToLower())
            {

                default:
                    return base.OnCommand(args);
            }
        }



        protected internal override void OnStop()
        {
            base.OnStop();
            Stop();
        }

        private bool OnUpgradeCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "wallet":
                    return OnUpgradeWalletCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnInstallCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("error");
                return true;
            }

            bool isTemp;
            string fileName;
            var pluginName = args[1];

            if (!File.Exists(pluginName))
            {
                if (string.IsNullOrEmpty(CliSettings.Default.PluginURL))
                {
                    Console.WriteLine("You must define `PluginURL` in your `config.json`");
                    return true;
                }

                var address = string.Format(CliSettings.Default.PluginURL, pluginName, typeof(Plugin).Assembly.GetVersion());
                fileName = Path.Combine(Path.GetTempPath(), $"{pluginName}.zip");
                isTemp = true;

                Console.WriteLine($"Downloading from {address}");
                using (WebClient wc = new WebClient())
                {
                    wc.DownloadFile(address, fileName);
                }
            }
            else
            {
                fileName = pluginName;
                isTemp = false;
            }

            try
            {
                ZipFile.ExtractToDirectory(fileName, ".");
            }
            catch (IOException)
            {
                Console.WriteLine($"Plugin already exist.");
                return true;
            }
            finally
            {
                if (isTemp)
                {
                    File.Delete(fileName);
                }
            }

            Console.WriteLine($"Install successful, please restart neo-cli.");
            return true;
        }

        private bool OnUnInstallCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("error");
                return true;
            }

            var pluginName = args[1];
            var plugin = Plugin.Plugins.FirstOrDefault(p => p.Name == pluginName);
            if (plugin is null)
            {
                Console.WriteLine("Plugin not found");
                return true;
            }

            File.Delete(plugin.Path);
            File.Delete(plugin.ConfigFile);
            try
            {
                Directory.Delete(Path.GetDirectoryName(plugin.ConfigFile), false);
            }
            catch (IOException)
            {
            }
            Console.WriteLine($"Uninstall successful, please restart neo-cli.");
            return true;
        }

        private bool OnUpgradeWalletCommand(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("error");
                return true;
            }
            string path = args[2];
            if (Path.GetExtension(path) != ".db3")
            {
                Console.WriteLine("Can't upgrade the wallet file.");
                return true;
            }
            if (!File.Exists(path))
            {
                Console.WriteLine("File does not exist.");
                return true;
            }
            string password = ReadUserInput("password", true);
            if (password.Length == 0)
            {
                Console.WriteLine("cancelled");
                return true;
            }
            string path_new = Path.ChangeExtension(path, ".json");
            if (File.Exists(path_new))
            {
                Console.WriteLine($"File '{path_new}' already exists");
                return true;
            }
            NEP6Wallet.Migrate(path_new, path, password, CliSettings.Default.Protocol).Save();
            Console.WriteLine($"Wallet file upgrade complete. New wallet file has been auto-saved at: {path_new}");
            return true;
        }

        public void OpenWallet(string path, string password)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException();
            }

            if (Path.GetExtension(path) == ".db3")
            {
                CurrentWallet = UserWallet.Open(path, password, CliSettings.Default.Protocol);
            }
            else
            {
                NEP6Wallet nep6wallet = new NEP6Wallet(path, CliSettings.Default.Protocol);
                nep6wallet.Unlock(password);
                CurrentWallet = nep6wallet;
            }
        }


        private void WriteBlocks(uint start, uint count, string path, bool writeStart)
        {
            uint end = start + count - 1;
            using FileStream fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
            if (fs.Length > 0)
            {
                byte[] buffer = new byte[sizeof(uint)];
                if (writeStart)
                {
                    fs.Seek(sizeof(uint), SeekOrigin.Begin);
                    fs.Read(buffer, 0, buffer.Length);
                    start += BitConverter.ToUInt32(buffer, 0);
                    fs.Seek(sizeof(uint), SeekOrigin.Begin);
                }
                else
                {
                    fs.Read(buffer, 0, buffer.Length);
                    start = BitConverter.ToUInt32(buffer, 0);
                    fs.Seek(0, SeekOrigin.Begin);
                }
            }
            else
            {
                if (writeStart)
                {
                    fs.Write(BitConverter.GetBytes(start), 0, sizeof(uint));
                }
            }
            if (start <= end)
                fs.Write(BitConverter.GetBytes(count), 0, sizeof(uint));
            fs.Seek(0, SeekOrigin.End);
            Console.WriteLine("Export block from " + start + " to " + end);

            using (var percent = new ConsolePercent(start, end))
            {
                for (uint i = start; i <= end; i++)
                {
                    Block block = NativeContract.Ledger.GetBlock(NeoSystem.StoreView, i);
                    byte[] array = block.ToArray();
                    fs.Write(BitConverter.GetBytes(array.Length), 0, sizeof(int));
                    fs.Write(array, 0, array.Length);
                    percent.Value = i;
                }
            }
        }

        private static void WriteLineWithoutFlicker(string message = "", int maxWidth = 80)
        {
            if (message.Length > 0) Console.Write(message);
            var spacesToErase = maxWidth - message.Length;
            if (spacesToErase < 0) spacesToErase = 0;
            Console.WriteLine(new string(' ', spacesToErase));
        }
    }
}
