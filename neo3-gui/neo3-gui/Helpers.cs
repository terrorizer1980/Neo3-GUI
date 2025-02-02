﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.WebSockets;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Neo.Common;
using Neo.Common.Consoles;
using Neo.Common.Json;
using Neo.Common.Storage;
using Neo.Common.Utility;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Models;
using Neo.Models.Transactions;
using Neo.Models.Wallets;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Services;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using Neo.Wallets;
using Neo.Wallets.SQLite;
using Boolean = Neo.VM.Types.Boolean;
using Buffer = Neo.VM.Types.Buffer;
using VmArray = Neo.VM.Types.Array;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Neo
{
    public static class Helpers
    {

        public static readonly JsonSerializerOptions SerializeOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IgnoreNullValues = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters =
            {
                new UInt160Converter(),
                new UInt256Converter(),
                new NumToStringConverter(),
                new BigDecimalConverter(),
                new BigIntegerConverter(),
                new DatetimeJsonConverter(),
                new ByteArrayConverter(),
                new JObjectConverter(),
            }
        };




        private static byte UTF8CharacterMask1Byte = 0b1000_0000;
        private static byte Valid1Byte = 0b0000_0000;//0b0xxx_xxxx

        private static byte UTF8CharacterMask2Byte = 0b1110_0000;
        private static byte Valid2Byte = 0b1100_0000;//0b110x_xxxx

        private static byte UTF8CharacterMask3Byte = 0b1111_0000;
        private static byte Valid3Byte = 0b1110_0000;//0b1110_xxxx

        private static byte UTF8CharacterMask4Byte = 0b1111_1000;
        private static byte Valid4Byte = 0b1111_0000;//0b1111_0xxx

        private static byte UTF8CharacterMaskForExtraByte = 0b1100_0000;
        private static byte ValidExtraByte = 0b1000_0000;//0b10xx_xxxx


        public static bool IsValidUTF8ByteArray(this byte[] bytes)
        {
            short extraByteCount = 0;

            foreach (byte bt in bytes)
            {

                if (extraByteCount > 0)
                {
                    extraByteCount--;

                    // Extra Byte Pattern.
                    if ((bt & UTF8CharacterMaskForExtraByte) != ValidExtraByte)
                        return false;
                    continue;
                }
                else
                {
                    // 1 Byte Pattern.
                    if ((bt & UTF8CharacterMask1Byte) == Valid1Byte)
                    {
                        continue;
                    }

                    // 2 Bytes Pattern.
                    if ((bt & UTF8CharacterMask2Byte) == Valid2Byte)
                    {
                        extraByteCount = 1;
                        continue;
                    }

                    // 3 Bytes Pattern.
                    if ((bt & UTF8CharacterMask3Byte) == Valid3Byte)
                    {
                        extraByteCount = 2;
                        continue;
                    }

                    // 4 Bytes Pattern.
                    if ((bt & UTF8CharacterMask4Byte) == Valid4Byte)
                    {
                        extraByteCount = 3;
                        continue;
                    }

                    // invalid UTF8-Bytes.
                    return false;
                }
            }

            return extraByteCount == 0;
        }




        /// <summary>
        /// do not close this snapshot!
        /// </summary>
        /// <returns></returns>
        public static DataCache GetDefaultSnapshot()
        {
            //while (Program.Starter.NeoSystem==null)
            //{
            //}
            return Program.Starter.NeoSystem.StoreView;
        }

        /// <summary>
        /// do not close this snapshot!
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public static DataCache GetDefaultSnapshot(this object x)
        {
            return Program.Starter.NeoSystem.StoreView;
        }


        /// <summary>
        /// get current height via default block chain instance
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static LocalNode GetDefaultLocalNode(this object obj)
        {
            return Program.Starter.LocalNode; ;
        }


        public static readonly Dictionary<string, ContractEventDescriptor> EventMetaCache = new Dictionary<string, ContractEventDescriptor>();
        public static ContractEventDescriptor GetEvent(this UInt160 contractHash, string eventName)
        {
            var cachekey = contractHash + eventName;
            if (EventMetaCache.ContainsKey(cachekey))
            {
                return EventMetaCache[cachekey];
            }
            var contract = GetDefaultSnapshot().GetContract(contractHash);
            var eventMeta = contract?.Manifest.Abi.Events.FirstOrDefault(e => e.Name == eventName);
            if (eventMeta == null)
            {
                return null;
            }

            EventMetaCache[cachekey] = eventMeta;
            return eventMeta;
        }

        /// <summary>
        /// Load configuration with different Environment Variable
        /// </summary>
        /// <param name="configFileName">Configuration</param>
        /// <returns>IConfigurationRoot</returns>
        public static string GetEnvConfigPath(this string configFileName)
        {
            var env = Environment.GetEnvironmentVariable("NEO_NETWORK");
            var configFile = string.IsNullOrWhiteSpace(env) ? $"{configFileName}.json" : $"{configFileName}.{env}.json";
            return configFile;
        }

        /// <summary>
        /// Load configuration with different Environment Variable
        /// </summary>
        /// <param name="config">Configuration</param>
        /// <returns>IConfigurationRoot</returns>
        public static IConfigurationRoot LoadConfig(this string config)
        {
            var configFile = config.GetEnvConfigPath();
            // Working directory
            var file = Path.Combine(Environment.CurrentDirectory, configFile);
            if (!File.Exists(file))
            {
                // EntryPoint folder
                file = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), configFile);
                if (!File.Exists(file))
                {
                    // neo.dll folder
                    file = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), configFile);
                    if (!File.Exists(file))
                    {
                        // default config
                        return new ConfigurationBuilder().Build();
                    }
                }
            }
            return new ConfigurationBuilder()
                .AddJsonFile(file, true)
                .Build();
        }


        public static string ToAddress(this UInt160 scriptHash)
        {
            return scriptHash.ToAddress(CliSettings.Default.Protocol.AddressVersion);
        }

        public static UInt160 ToScriptHash(this string address)
        {
            return address.ToScriptHash(CliSettings.Default.Protocol.AddressVersion);
        }

        public static string GetVersion(this Assembly assembly)
        {
            CustomAttributeData attribute = assembly.CustomAttributes.FirstOrDefault(p => p.AttributeType == typeof(AssemblyInformationalVersionAttribute));
            if (attribute == null) return assembly.GetName().Version.ToString(3);
            return (string)attribute.ConstructorArguments[0].Value;
        }

        public static bool ToBool(this string input)
        {
            if (input == null) return false;

            input = input.ToLowerInvariant();

            return input == "true" || input == "yes" || input == "1";
        }

        public static bool IsYes(this string input)
        {
            if (input == null) return false;

            input = input.ToLowerInvariant();

            return input == "yes" || input == "y";
        }


        /// <summary>
        /// broadcast transaction and cache
        /// </summary>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static async Task Broadcast(this Transaction tx)
        {
            Program.Starter.NeoSystem.Blockchain.Tell(tx);
            //.Tell(new LocalNode.Relay { Inventory = tx });
            var task = Task.Run(() => UnconfirmedTransactionCache.AddTransaction(tx));
        }



        /// <summary>
        /// safe serialize signContext to avoid encoding issues
        /// </summary>
        /// <param name="signContext"></param>
        /// <returns></returns>
        public static string SafeSerialize(this ContractParametersContext signContext)
        {
            return signContext.ToJson().SerializeJson();
        }

        /// <summary>
        /// create tx by script and signer
        /// </summary>
        /// <param name="wallet"></param>
        /// <param name="script"></param>
        /// <param name="signers"></param>
        /// <returns></returns>
        public static Transaction InitTransaction(this Wallet wallet, byte[] script, UInt160 sender = null, params UInt160[] signers)
        {
            var cosigners = signers.Select(account => new Signer { Account = account, Scopes = WitnessScope.Global }).ToArray();
            return InitTransaction(wallet, script, sender, cosigners);
        }

        /// <summary>
        /// create tx by script and signer
        /// </summary>
        /// <param name="wallet"></param>
        /// <param name="script"></param>
        /// <param name="sender"></param>
        /// <param name="signers"></param>
        /// <returns></returns>
        public static Transaction InitTransaction(this Wallet wallet, byte[] script, UInt160 sender = null, params Signer[] signers)
        {
            var tx = wallet.MakeTransaction(GetDefaultSnapshot(), script, sender, signers, maxGas: 2000_00000000);
            return tx;
        }


        /// <summary>
        /// append sign to signContext
        /// </summary>
        /// <param name="wallet"></param>
        /// <param name="signContext"></param>
        /// <returns></returns>
        public static bool SignContext(this Wallet wallet, ContractParametersContext signContext)
        {
            wallet.Sign(signContext);
            return signContext.Completed;
        }

        /// <summary>
        /// sign transaction
        /// </summary>
        /// <param name="wallet"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static (bool, ContractParametersContext) TrySignTx(this Wallet wallet, Transaction tx)
        {
            var context = new ContractParametersContext(GetDefaultSnapshot(), tx, CliSettings.Default.Protocol.Network);
            var signResult = wallet.SignContext(context);
            if (signResult)
            {
                tx.Witnesses = context.GetWitnesses();
            }
            return (signResult, context);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public static async Task SendAsync(this WebSocket socket, object data)
        {
            if (data == null)
            {
                return;
            }

            var bytes = SerializeJsonBytes(data);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }


        /// <summary>
        /// serialize to utf8 json bytes, more performance than to json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static byte[] SerializeJsonBytes<T>(this T obj)
        {
            return JsonSerializer.SerializeToUtf8Bytes(obj, SerializeOptions);
        }

        /// <summary>
        /// serialize to utf8 json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static string SerializeJson<T>(this T obj)
        {
            return JsonSerializer.Serialize(obj, SerializeOptions);
        }



        /// <summary>
        /// deserialize from json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static T DeserializeJson<T>(this string json)
        {
            if (json.IsNull())
            {
                return default(T);
            }

            return JsonSerializer.Deserialize<T>(json, SerializeOptions);
        }



        /// <summary>
        /// deserialize from json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="targetType"></param>
        /// <returns></returns>
        public static object DeserializeJson(this string json, Type targetType)
        {
            return JsonSerializer.Deserialize(json, targetType, SerializeOptions);
        }



        /// <summary>
        /// deserialize from json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="jsonBytes"></param>
        /// <returns></returns>
        public static T DeserializeJson<T>(this byte[] jsonBytes)
        {
            if (jsonBytes.IsEmpty())
            {
                return default(T);
            }

            return JsonSerializer.Deserialize<T>(jsonBytes, SerializeOptions);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="services"></param>
        public static void AddWebSocketInvoker(this IServiceCollection services)
        {
            services.AddSingleton<WebSocketHub>();
            services.AddSingleton<WebSocketHubMiddleware>();
            services.AddSingleton<WebSocketSession>();
            services.AddSingleton<WebSocketExecutor>();
            var interfaceType = typeof(IApiService);
            var assembly = interfaceType.Assembly;
            foreach (var type in assembly.GetExportedTypes()
                .Where(t => !t.IsAbstract && interfaceType.IsAssignableFrom(t)))
            {
                services.AddSingleton(type);
            }
        }

        /// <summary>
        /// change to utc Time without change time
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public static DateTime AsUtcTime(this DateTime time)
        {
            return DateTime.SpecifyKind(time, DateTimeKind.Utc);
        }


        private static readonly Dictionary<Type, object> defaultValues = new Dictionary<Type, object>();

        /// <summary>
        /// get input type default value
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static object GetDefaultValue(this Type type)
        {
            if (defaultValues.ContainsKey(type))
            {
                return defaultValues[type];
            }

            if (type.IsValueType)
            {
                var val = Activator.CreateInstance(type);
                defaultValues[type] = val;
                return val;
            }

            return null;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static UInt160 ToUInt160(this byte[] bytes)
        {
            if (bytes?.Length == 20)
            {
                return new UInt160(bytes);
            }
            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string TryToAddress(this byte[] bytes)
        {
            if (bytes?.Length == 20)
            {
                return new UInt160(bytes).ToAddress();
            }
            return null;
        }


        /// <summary>
        /// convert private key string(wif or hex) to bytes
        /// </summary>
        /// <param name="privateKey"></param>
        /// <returns></returns>
        public static byte[] ToPrivateKeyBytes(this string privateKey)
        {
            byte[] keyBytes;
            try
            {
                keyBytes = Wallet.GetPrivateKeyFromWIF(privateKey);
            }
            catch
            {
                keyBytes = privateKey.HexToBytes();
            }

            return keyBytes;
        }


        /// <summary>
        /// string is null or white space
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static bool IsNull(this string text)
        {
            return string.IsNullOrWhiteSpace(text);
        }


        /// <summary>
        /// string is not null or white space
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static bool NotNull(this string text)
        {
            return !IsNull(text);
        }

        /// <summary>
        /// collection is null or empty
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static bool IsEmpty<T>(this IEnumerable<T> source)
        {
            return source == null || !source.Any();
        }


        /// <summary>
        /// collection is not null or empty
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static bool NotEmpty<T>(this IEnumerable<T> source)
        {
            return !source.IsEmpty();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="account"></param>
        /// <param name="snapshot"></param>
        /// <returns></returns>
        public static AccountType GetAccountType(this WalletAccount account, DataCache snapshot)
        {
            if (account.Contract != null)
            {
                if (account.Contract.Script.IsMultiSigContract(out _, out int _))
                {
                    return AccountType.MultiSignature;
                }
                if (account.Contract.Script.IsSignatureContract())
                {
                    return AccountType.Standard;
                }
                if (snapshot.GetContract(account.Contract.ScriptHash) != null)
                {
                    return AccountType.DeployedContract;
                }
            }
            return AccountType.NonStandard;
        }

        /// <summary>
        /// query neo/gas balance quickly
        /// </summary>
        /// <param name="addresses"></param>
        /// <param name="asset"></param>
        /// <param name="snapshot"></param>
        /// <returns></returns>
        public static List<BigDecimal> GetNativeBalanceOf<T>(this IEnumerable<UInt160> addresses, FungibleToken<T> asset, DataCache snapshot) where T : AccountState, new()
        {
            var balances = new List<BigDecimal>();
            foreach (var account in addresses)
            {
                var balance = asset.BalanceOf(snapshot, account);
                balances.Add(new BigDecimal(balance, asset.Decimals));
            }
            return balances;
        }

        /// <summary>
        /// query balance
        /// </summary>
        /// <param name="addresses"></param>
        /// <param name="assetId"></param>
        /// <returns></returns>
        public static List<BigDecimal> GetBalanceOf(this IEnumerable<UInt160> addresses, UInt160 assetId)
        {
            //using var snapshot = Blockchain..Singleton.GetSnapshot();
            return GetBalanceOf(addresses, assetId, GetDefaultSnapshot());
        }



        /// <summary>
        /// query balance
        /// </summary>
        /// <param name="addresses"></param>
        /// <param name="assetId"></param>
        /// <param name="snapshot"></param>
        /// <returns></returns>
        public static List<BigDecimal> GetBalanceOf(this IEnumerable<UInt160> addresses, UInt160 assetId, DataCache snapshot)
        {
            var assetInfo = AssetCache.GetAssetInfo(assetId, snapshot);
            if (assetInfo == null)
            {
                throw new ArgumentException($"invalid assetId:[{assetId}]");
            }

            if (assetInfo.Asset == NativeContract.NEO.Hash)
            {
                return GetNativeBalanceOf(addresses, NativeContract.NEO, snapshot);
            }
            if (assetInfo.Asset == NativeContract.GAS.Hash)
            {
                return GetNativeBalanceOf(addresses, NativeContract.GAS, snapshot);
            }
            using var sb = new ScriptBuilder();
            foreach (var address in addresses)
            {
                sb.EmitDynamicCall(assetId, "balanceOf", address);
            }

            using ApplicationEngine engine = sb.ToArray().RunTestMode(snapshot);
            if (engine.State.HasFlag(VMState.FAULT))
            {
                throw new Exception($"query balance error");
            }

            var result = engine.ResultStack.Select(p => p.GetInteger());
            return result.Select(bigInt => new BigDecimal(bigInt, assetInfo.Decimals)).ToList();
        }

        /// <summary>
        /// query balance
        /// </summary>
        /// <param name="address"></param>
        /// <param name="assetId"></param>
        /// <returns></returns>
        public static BigDecimal GetBalanceOf(this UInt160 address, UInt160 assetId)
        {
            //using var snapshot = Blockchai.Singleton.GetSnapshot();
            return GetBalanceOf(address, assetId, GetDefaultSnapshot());
        }

        /// <summary>
        /// query balance
        /// </summary>
        /// <param name="address"></param>
        /// <param name="assetId"></param>
        /// <param name="snapshot"></param>
        /// <returns></returns>
        public static BigDecimal GetBalanceOf(this UInt160 address, UInt160 assetId, DataCache snapshot)
        {
            var assetInfo = AssetCache.GetAssetInfo(assetId, snapshot);
            if (assetInfo == null)
            {
                return new BigDecimal(BigInteger.Zero, 0);
            }

            using var sb = new ScriptBuilder();
            sb.EmitDynamicCall(assetId, "balanceOf", address);
            using var engine = sb.ToArray().RunTestMode(snapshot);
            if (engine.State.HasFlag(VMState.FAULT))
            {
                return new BigDecimal(BigInteger.Zero, 0);
            }
            var balances = engine.ResultStack.Pop().GetInteger();
            return new BigDecimal(balances, assetInfo.Decimals);
        }


        /// <summary>
        /// sum same asset amount
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static BigDecimal SumAssetAmount(this IEnumerable<BigDecimal> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException();
            }

            var item = source.FirstOrDefault();
            var total = source.Select(s => s.Value).Sum();
            return new BigDecimal(total, item.Decimals);
        }



        /// <summary>
        /// convert bigint to asset decimal value
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="assetId"></param>
        /// <returns></returns>
        public static (BigDecimal amount, AssetInfo asset) GetAssetAmount(this BigInteger amount, UInt160 assetId)
        {
            var asset = AssetCache.GetAssetInfo(assetId);
            if (asset == null)
            {
                return (new BigDecimal(BigInteger.Zero, 0), null);
            }

            return (new BigDecimal(amount, asset.Decimals), asset);
        }

        /// <summary>
        /// convert to Big Endian hex string without "Ox"
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static string ToBigEndianHex(this UInt160 address)
        {
            return address.ToArray().ToHexString(reverse: true);
        }

        /// <summary>
        /// convert to Big Endian hex string without "Ox"
        /// </summary>
        /// <param name="txId"></param>
        /// <returns></returns>
        public static string ToBigEndianHex(this UInt256 txId)
        {
            return txId.ToArray().ToHexString(reverse: true);
        }

        private static readonly DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);


        /// <summary>
        /// 
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public static DateTime FromTimestampMS(this ulong timestamp)
        {
            return unixEpoch.AddMilliseconds(timestamp);
        }

        public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> expr1,
            Expression<Func<T, bool>> expr2)
        {
            var invokedExpr = Expression.Invoke(expr2, expr1.Parameters);
            return Expression.Lambda<Func<T, bool>>
                (Expression.OrElse(expr1.Body, invokedExpr), expr1.Parameters);
        }

        public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> expr1,
            Expression<Func<T, bool>> expr2)
        {
            if (expr1 == null)
            {
                return expr2;
            }

            var invokedExpr = Expression.Invoke(expr2, expr1.Parameters);
            return Expression.Lambda<Func<T, bool>>
                (Expression.AndAlso(expr1.Body, invokedExpr), expr1.Parameters);
        }


        private static readonly ConcurrentDictionary<ErrorCode, string> _errorMap = new ConcurrentDictionary<ErrorCode, string>();

        public static WsError ToError(this ErrorCode code)
        {
            if (!_errorMap.ContainsKey(code))
            {
                var message = GetErrorMsg(code);
                _errorMap[code] = message ?? code.ToString();
            }

            return new WsError()
            {
                Code = (int)code,
                Message = _errorMap[code],
            };
        }

        private static string GetErrorMsg(this ErrorCode errorCode)
        {
            FieldInfo fieldInfo = errorCode.GetType().GetField(errorCode.ToString());
            var desc = fieldInfo.GetCustomAttribute<DescriptionAttribute>();
            return desc?.Description;
        }

        public static bool NotVmByteArray(this StackItem item)
        {
            return !(item is ByteString);
        }

        public static bool NotVmNull(this StackItem item)
        {
            return !(item.IsNull);
        }

        public static bool IsVmNullOrByteArray(this StackItem item)
        {
            return item.IsNull || item is ByteString;
        }

        public static bool NotVmInt(this StackItem item)
        {
            return !(item is Integer);
        }


        /// <summary>
        /// try to convert "Transfer" event, missing "Decimals"、"Symbol"
        /// </summary>
        /// <param name="notification"></param>
        /// <returns></returns>
        public static TransferNotifyItem ConvertToTransfer(this NotifyEventArgs notification)
        {
            if (!"transfer".Equals(notification.EventName, StringComparison.OrdinalIgnoreCase) || notification.State.Count < 3)
            {
                return null;
            }
            var notify = notification.State;
            var fromItem = notify[0];
            var toItem = notify[1];
            var amountItem = notify[2];
            if (!fromItem.IsVmNullOrByteArray() || !toItem.IsVmNullOrByteArray())
            {
                return null;
            }
            var from = fromItem.GetByteSafely();
            if (from != null && from.Length != UInt160.Length)
            {
                return null;
            }
            var to = toItem.GetByteSafely();
            if (to != null && to.Length != UInt160.Length)
            {
                return null;
            }
            if (from == null && to == null)
            {
                return null;
            }
            if (amountItem.NotVmByteArray() && amountItem.NotVmInt())
            {
                return null;
            }
            var amount = amountItem.ToBigInteger();
            if (amount == null)
            {
                return null;
            }
            var record = new TransferNotifyItem
            {
                From = from == null ? null : new UInt160(from),
                To = to == null ? null : new UInt160(to),
                Amount = amount.Value,
                Asset = notification.ScriptHash,
            };
            return record;
        }


        public static TransferNotifyItem GetTransferNotify(this VmArray notifyArray, UInt160 asset)
        {
            if (notifyArray.Count < 4) return null;
            // Event name should be encoded as a byte array.
            if (notifyArray[0].NotVmByteArray()) return null;

            var eventName = notifyArray[0].GetString();
            if (!"Transfer".Equals(eventName, StringComparison.OrdinalIgnoreCase)) return null;

            var fromItem = notifyArray[1];
            if (fromItem.NotVmByteArray() && fromItem.NotVmNull()) return null;

            var fromBytes = fromItem.GetByteSafely();
            if (fromBytes?.Length != 20)
            {
                fromBytes = null;
            }

            var toItem = notifyArray[2];
            if (toItem != null && toItem.NotVmByteArray()) return null;

            byte[] toBytes = toItem.GetByteSafely();
            if (toBytes?.Length != 20)
            {
                toBytes = null;
            }
            if (fromBytes == null && toBytes == null) return null;

            var amountItem = notifyArray[3];
            if (amountItem.NotVmByteArray() && amountItem.NotVmInt()) return null;

            var transfer = new TransferNotifyItem()
            {
                Asset = asset,
                From = fromBytes == null ? null : new UInt160(fromBytes),
                To = new UInt160(toBytes),
                Amount = amountItem.GetInteger(),
            };
            return transfer;
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public static TransactionPreviewModel ToTransactionPreviewModel(this UnconfirmedTransactionCache.TempTransaction transaction)
        {
            return new TransactionPreviewModel()
            {
                TxId = transaction.Tx.Hash,
                Transfers = transaction.Transfers?.Select(tran => tran.ToTransferModel()).ToList()
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="transfer"></param>
        /// <returns></returns>
        public static TransferModel ToTransferModel(this TransferNotifyItem transfer)
        {
            return new TransferModel()
            {
                From = transfer.From,
                To = transfer.To,
                Amount = new BigDecimal(transfer.Amount, transfer.Decimals).ToString(),
                Symbol = transfer.Symbol,
            };
        }


        public static byte[] GetByteSafely(this StackItem item)
        {
            try
            {
                switch (item)
                {
                    case Null _:
                        return null;
                }
                return item?.GetSpan().ToArray();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// try get private key from hex string or wif,error will return null
        /// </summary>
        /// <param name="privateKey"></param>
        /// <returns></returns>
        public static byte[] TryGetPrivateKey(this string privateKey)
        {
            if (privateKey.IsNull())
            {
                return null;
            }
            try
            {
                return Wallet.GetPrivateKeyFromWIF(privateKey);
            }
            catch (FormatException)
            {
            }
            if (privateKey.Length == 64)
            {
                try
                {
                    return privateKey.HexToBytes();
                }
                catch (Exception e)
                {
                }
            }
            return null;
        }

        /// <summary>
        /// convert to friendly transfer model
        /// </summary>
        /// <param name="transfer"></param>
        /// <returns></returns>
        public static TransferModel ToTransferModel(this TransferInfo transfer)
        {
            var tran = new TransferModel()
            {
                From = transfer.From,
                To = transfer.To,
            };
            var (amount, asset) = transfer.Amount.GetAssetAmount(transfer.Asset);
            tran.Amount = amount.ToString();
            tran.Symbol = asset.Symbol;
            return tran;
        }

        /// <summary>
        /// convert to AddressBalanceModel list
        /// </summary>
        /// <param name="balances"></param>
        /// <returns></returns>
        public static List<AddressBalanceModel> ToAddressBalanceModels(this ILookup<UInt160, BalanceInfo> balances)
        {
            return balances.Select(b => new AddressBalanceModel()
            {
                AddressHash = b.Key,
                Balances = b.Select(assetBalance => new AssetBalanceModel()
                {
                    Asset = assetBalance.Asset,
                    Symbol = assetBalance.AssetSymbol,
                    Balance = new BigDecimal(assetBalance.Balance, assetBalance.AssetDecimals),
                }).ToList(),
            }).ToList();
        }


        public static BigDecimal ToNeo(this BigInteger amount)
        {
            return new BigDecimal(amount, NativeContract.NEO.Decimals);
        }

        public static BigDecimal ToGas(this BigInteger amount)
        {
            return new BigDecimal(amount, NativeContract.GAS.Decimals);
        }



        public static List<TransactionPreviewModel> ToTransactionPreviewModel(this IEnumerable<TransferInfo> trans)
        {
            return trans.ToLookup(x => x.TxId).Select(ToTransactionPreviewModel).ToList();
        }

        private static TransactionPreviewModel ToTransactionPreviewModel(IGrouping<UInt256, TransferInfo> lookup)
        {
            var item = lookup.FirstOrDefault();
            var model = new TransactionPreviewModel()
            {
                TxId = lookup.Key,
                Timestamp = item.TimeStamp,
                BlockHeight = item.BlockHeight,
                Transfers = lookup.Select(x => x.ToTransferModel()).ToList(),
            };
            return model;
        }


        public static Contract ToVerificationContract(this ECPoint point)
        {
            var contract = Contract.CreateSignatureContract(point);
            return contract;
        }


        public static BigInteger? ToBigInteger(this StackItem value) => value == null || value is Null ? (BigInteger?)null : value.GetInteger();

        public static bool ToBigInteger(this JStackItem item, out BigInteger amount)
        {
            amount = 0;
            if (item.TypeCode == ContractParameterType.Integer)
            {
                amount = (BigInteger)item.Value;
                return true;
            }
            if (item.TypeCode == ContractParameterType.ByteArray)
            {
                amount = new BigInteger((byte[])item.Value);
                return true;
            }
            return false;
        }


        public static byte[] Append(this byte[] source, params byte[][] bytes)
        {
            IEnumerable<byte> data = source;
            foreach (var b in bytes)
            {
                data = data.Concat(b);
            }
            return data.ToArray();
        }

        public static NotificationInfo ToNotificationInfo(this NotifyEventArgs notify)
        {
            var notification = new NotificationInfo();
            notification.EventName = notify.EventName;
            notification.Contract = notify.ScriptHash;
            notification.State = notify.State.ToJson();
            return notification;
        }

        public static ApplicationEngine RunTestMode(this byte[] script, DataCache snapshot, IVerifiable container = null)
        {
            return ApplicationEngine.Run(script, snapshot ?? GetDefaultSnapshot(), container, settings: CliSettings.Default.Protocol, gas: Constant.TestMode);
        }

        public static ContractState GetContract(this DataCache snapshot, UInt160 hash)
        {
            return NativeContract.ContractManagement.GetContract(snapshot, hash);
        }


        /// <summary>
        /// get current height via default block chain instance
        /// </summary>
        /// <returns></returns>
        public static uint GetCurrentHeight()
        {
            return GetDefaultSnapshot().GetHeight();
        }

        /// <summary>
        /// get current height via default block chain instance
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static uint GetCurrentHeight(this object obj)
        {
            return GetCurrentHeight();
        }

        /// <summary>
        /// get current height via default block chain instance
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static uint GetCurrentHeaderHeight(this object obj)
        {
            return Program.Starter.NeoSystem.HeaderCache.Last?.Index ?? GetCurrentHeight();
        }


        public static uint GetHeight(this DataCache snapshot)
        {
            return NativeContract.Ledger.CurrentIndex(snapshot);
        }

        public static Header GetCurrentHeader(this DataCache snapshot)
        {
            return NativeContract.Ledger.GetHeader(snapshot, snapshot.GetHeight());
        }

        /// <summary>
        /// get block via default block chain instance
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public static Block GetBlock(this uint index)
        {
            return NativeContract.Ledger.GetBlock(GetDefaultSnapshot(), index);
        }

        /// <summary>
        /// get block via default block chain instance
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static Block GetBlock(this UInt256 hash)
        {
            return NativeContract.Ledger.GetBlock(GetDefaultSnapshot(), hash);
        }


        /// <summary>
        /// get Contract via default block chain instance
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static ContractState GetContract(this UInt160 hash)
        {
            return GetDefaultSnapshot().GetContract(hash);
        }

        public static Block GetBlock(this DataCache snapshot, uint index)
        {
            return NativeContract.Ledger.GetBlock(snapshot, index);
        }

        public static Header GetHeader(this DataCache snapshot, uint index)
        {
            return NativeContract.Ledger.GetHeader(snapshot, index);
        }

        public static Header GetHeader(this DataCache snapshot, UInt256 hash)
        {
            return NativeContract.Ledger.GetHeader(snapshot, hash);
        }

        public static Block GetBlock(this DataCache snapshot, UInt256 hash)
        {
            return NativeContract.Ledger.GetBlock(snapshot, hash);
        }

        public static Transaction GetTransaction(this DataCache snapshot, UInt256 hash)
        {
            return NativeContract.Ledger.GetTransaction(snapshot, hash);
        }

        public static TransactionState GetTransactionState(this DataCache snapshot, UInt256 hash)
        {
            return NativeContract.Ledger.GetTransactionState(snapshot, hash);
        }

        public static string GetExMessage(this Exception ex)
        {
            var msg = ex.Message;
            while (ex.InnerException != null)
            {
                msg += $"\r\n----[{ex.InnerException.Message}]";
                ex = ex.InnerException;
            }
            return msg;
        }

        /// <summary>
        /// Converts the <see cref="StackItem"/> to a <see cref="ContractParameter"/>.
        /// </summary>
        /// <param name="item">The <see cref="StackItem"/> to convert.</param>
        /// <returns>The converted <see cref="ContractParameter"/>.</returns>
        public static ContractParameter ToContractParameter(this StackItem item, List<(StackItem, ContractParameter)> context = null)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));

            ContractParameter parameter = null;
            switch (item)
            {
                case VmArray array:
                    if (context is null)
                        context = new List<(StackItem, ContractParameter)>();
                    else
                        (_, parameter) = context.FirstOrDefault(p => ReferenceEquals(p.Item1, item));
                    if (parameter is null)
                    {
                        parameter = new ContractParameter { Type = ContractParameterType.Array };
                        context.Add((item, parameter));
                        parameter.Value = array.Select(p => ToContractParameter(p, context)).ToList();
                    }
                    break;
                case Map map:
                    if (context is null)
                        context = new List<(StackItem, ContractParameter)>();
                    else
                        (_, parameter) = context.FirstOrDefault(p => ReferenceEquals(p.Item1, item));
                    if (parameter is null)
                    {
                        parameter = new ContractParameter { Type = ContractParameterType.Map };
                        context.Add((item, parameter));
                        parameter.Value = map.Select(p =>
                            new KeyValuePair<ContractParameter, ContractParameter>(ToContractParameter(p.Key, context),
                                ToContractParameter(p.Value, context))).ToList();
                    }
                    break;
                case Boolean _:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.Boolean,
                        Value = item.GetBoolean()
                    };
                    break;
                case ByteString array:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.ByteArray,
                        Value = array.GetSpan().ToArray()
                    };
                    break;
                case Buffer array:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.ByteArray,
                        Value = array.GetSpan().ToArray()
                    };
                    break;
                case Integer i:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.Integer,
                        Value = i.GetInteger()
                    };
                    break;
                case InteropInterface _:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.InteropInterface
                    };
                    break;
                case Null _:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.Any
                    };
                    break;
                default:
                    throw new ArgumentException($"StackItemType({item.Type}) is not supported to ContractParameter.");
            }
            return parameter;
        }
    }
}
