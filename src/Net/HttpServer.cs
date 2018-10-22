﻿namespace WhMgr.Net
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Security.Principal;
    using System.Text;
    using System.Threading;

    using Newtonsoft.Json;

    using WhMgr.Diagnostics;
    using WhMgr.Net.Models;
    using WhMgr.Net.Models.Providers;

    public class HttpServer
    {
        #region Variables

        private static readonly IEventLogger _logger = EventLogger.GetLogger();
        private readonly HttpListener _server;
        private Thread _requestThread;

        #endregion

        #region Properties

        public ushort Port { get; }

        public bool IsDebug { get; set; }

        public bool SkipEggs { get; set; }

        public MapProviderType MapProvider { get; }

        public MapProviderFork MapProviderFork { get; }

        #endregion

        #region Events

        public event EventHandler<PokemonDataEventArgs> PokemonReceived;

        private void OnPokemonReceived(PokemonData pokemon) => PokemonReceived?.Invoke(this, new PokemonDataEventArgs(pokemon));

        public event EventHandler<RaidDataEventArgs> RaidReceived;

        private void OnRaidReceived(RaidData raid) => RaidReceived?.Invoke(this, new RaidDataEventArgs(raid));

        #endregion

        #region Constructor

        public HttpServer(ushort port, MapProviderType provider, MapProviderFork fork)
        {
            Port = port;
            MapProvider = provider;
            MapProviderFork = fork;

            _server = new HttpListener();

            Initialize();
        }

        #endregion

        #region Public Methods

        public void Start()
        {
            _logger.Trace($"HttpServer::Start");

            if (_server.IsListening)
            {
                _logger.Debug($"HttpServer is already listening, failed to start...");
                return;
            }

            _logger.Info($"HttpServer is starting...");
            _server.Start();

            if (_server.IsListening)
            {
                _logger.Debug($"Listening on port {Port}...");
            }

            _logger.Info($"Starting HttpServer request handler...");
            _requestThread = new Thread(RequestHandler) { IsBackground = true };
            _requestThread.Start();
        }

        public void Stop()
        {
            _logger.Trace($"HttpServer::Stop");

            if (!_server.IsListening)
            {
                _logger.Debug($"HttpServer is not running, failed to stop...");
                return;
            }

            _logger.Info($"HttpServer is stopping...");
            _server.Stop();

            if (_requestThread != null)
            {
                _requestThread.Abort();
                _requestThread = null;
            }
        }

        #endregion

        #region Data Parsing Methods

        private void RequestHandler()
        {
            while (true)
            {
                var context = _server.GetContext();
                var response = context.Response;

                using (var sr = new StreamReader(context.Request.InputStream))
                {
                    var data = sr.ReadToEnd();
                    ParseData(data);
                }

                var buffer = Encoding.UTF8.GetBytes(Strings.DefaultResponseMessage);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.Close();

                Thread.Sleep(10);
            }
        }

        private void ParseData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return;

            try
            {
                if (IsDebug)
                {
                    File.AppendAllText(Strings.DebugLogFileName, data + Environment.NewLine);
                }

                var messages = JsonConvert.DeserializeObject<List<WebhookMessage>>(data);
                if (messages == null)
                    return;

                foreach (var message in messages)
                {
                    switch (message.Type)
                    {
                        case PokemonData.WebHookHeader:
                            ParsePokemon(message.Message);
                            break;
                        //case "gym":
                        //    ParseGym(message);
                        //    break;
                        //case "gym-info":
                        //case "gym_details":
                        //    ParseGymInfo(message);
                        //    break;
                        //case "egg":
                        case RaidData.WebHookHeader:
                            ParseRaid(message.Message);
                            break;
                        //case "tth":
                        //case "scheduler":
                        //    ParseTth(message);
                        //    break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                _logger.Debug(data);
            }
        }

        private void ParsePokemon(dynamic message)
        {
            try
            {
                var pokemon = JsonConvert.DeserializeObject<PokemonData>(message);
                //switch (MapProvider)
                //{
                //    case MapProviderType.Monocle:
                //        break;
                //    case MapProviderType.RealDeviceMap:
                //    case MapProviderType.RocketMap:
                //        switch (MapProviderFork)
                //        {
                //            //case MapProviderFork.Default:
                //            default:
                //                pokemon = JsonConvert.DeserializeObject<RealDeviceMapPokemon>(message);
                //                break;
                //        }
                //        break;
                //}

                if (pokemon == null)
                {
                    _logger.Error($"Failed to parse Pokemon webhook object: {message}");
                    return;
                }

                OnPokemonReceived(pokemon);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                _logger.Debug(message);
            }
        }

        private void ParseRaid(dynamic message)
        {
            try
            {
                var raid = JsonConvert.DeserializeObject<RaidData>(message);
                //switch (MapProvider)
                //{
                //    case MapProviderType.Monocle:
                //        break;
                //    case MapProviderType.RealDeviceMap:
                //    case MapProviderType.RocketMap:
                //        switch (MapProviderFork)
                //        {
                //            //case MapProviderFork.Default:
                //            default:
                //                raid = JsonConvert.DeserializeObject<RaidData>(message);
                //                break;
                //        }
                //        break;
                //}

                if (raid == null)
                {
                    _logger.Error($"Failed to parse Pokemon webhook object: {message}");
                    return;
                }

                if (SkipEggs && raid.PokemonId == 0)
                {
                    _logger.Debug($"Level {raid.Level} Egg, skipping...");
                    return;
                }

                raid.SetTimes();

                OnRaidReceived(raid);
            }
            catch (Exception ex)
            {
                _logger.Error(ex.StackTrace);
                _logger.Debug(message);
            }
        }

        //private void ParseGym(dynamic message)
        //{
        //    try
        //    {
        //        long raidActiveUtil = Convert.ToInt64(Convert.ToString(message["raid_active_until"]));
        //        string gymId = Convert.ToString(message["gym_id"]);
        //        int teamId = Convert.ToInt32(Convert.ToString(message["team_id"]));
        //        long lastModified = Convert.ToInt64(Convert.ToString(message["last_modified"]));
        //        int slotsAvailable = Convert.ToInt32(Convert.ToString(message["slots_available"]));
        //        int guardPokemonId = Convert.ToInt32(Convert.ToString(message["guard_pokemon_id"]));
        //        bool enabled = Convert.ToBoolean(Convert.ToString(message["enabled"]));
        //        double latitude = Convert.ToDouble(Convert.ToString(message["latitude"]));
        //        double longitude = Convert.ToDouble(Convert.ToString(message["longitude"]));
        //        double lowestPokemonMotivation = Convert.ToDouble(Convert.ToString(message["lowest_pokemon_motivation"]));
        //        int totalCp = Convert.ToInt32(Convert.ToString(message["total_cp"]));
        //        long occupiedSince = Convert.ToInt64(Convert.ToString(message["occupied_since"]));

        //        Log($"Raid Active Until: {new DateTime(TimeSpan.FromSeconds(raidActiveUtil).Ticks)}");
        //        Log($"Gym Id: {gymId}");
        //        Log($"Team Id: {teamId}");
        //        Log($"Last Modified: {lastModified}");
        //        Log($"Slots Available: {slotsAvailable}");
        //        Log($"Guard Pokemon Id: {guardPokemonId}");
        //        Log($"Enabled: {enabled}");
        //        Log($"Latitude: {latitude}");
        //        Log($"Longitude: {longitude}");
        //        Log($"Lowest Pokemon Motivation: {lowestPokemonMotivation}");
        //        Log($"Total CP: {totalCp}");
        //        Log($"Occupied Since: {new DateTime(TimeSpan.FromSeconds(occupiedSince).Ticks)}");//new DateTime(occupiedSince)}");
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Error(ex);
        //    }
        //}

        //private void ParseGymInfo(dynamic message)
        //{
        //    try
        //    {
        //        //| Field | Details | Example | | ————- | —————————————————— | ———————– |
        //        //id | The gym’s unique ID | "MzcwNGE0MjgyNThiNGE5NWFkZWIwYTBmOGM1Yzc2ODcuMTE=" |
        //        //name | The gym’s name | "St.Clements Church" |
        //        //description | The gym’s description | "" |
        //        //url | A URL to the gym’s image | "http://lh3.googleusercontent.com/image_url" |
        //        //latitude | The gym’s latitude | 43.633181 |
        //        //longitude | The gym’s longitude | 5.296836 |
        //        //team | The team that currently controls the gym | 1 |
        //        //pokemon | An array containing the Pokémon currently in the gym | [] |

        //        string id = Convert.ToString(message["id"]);
        //        string name = Convert.ToString(message["name"]);
        //        string description = Convert.ToString(message["description"]);
        //        string url = Convert.ToString(message["url"]);
        //        double latitude = Convert.ToDouble(Convert.ToString(message["latitude"]));
        //        double longitude = Convert.ToDouble(Convert.ToString(message["longitude"]));
        //        int team = Convert.ToInt32(Convert.ToString(message["team"]));
        //        dynamic pokemon = message["pokemon"];

        //        //Gym Pokémon:
        //        //Field | Details | Example | | ————————– | ——————————————————– | ———— |
        //        //trainer_name | The name of the trainer that the Pokémon belongs to | "johndoe9876" |
        //        //trainer_level | The trainer’s level1 | 34 |
        //        //pokemon_uid | The Pokémon’s unique ID | 4348002772281054056 |
        //        //pokemon_id | The Pokémon’s ID | 242 |
        //        //cp | The Pokémon’s base CP | 2940 |
        //        //cp_decayed | The Pokémon’s current CP | 115 |
        //        //stamina_max | The Pokémon’s max stamina | 500 |
        //        //stamina | The Pokémon’s current stamina | 500 |
        //        //move_1 | The Pokémon’s quick move | 234 |
        //        //move_2 | The Pokémon’s charge move | 108 |
        //        //height | The Pokémon’s height | 1.746612787246704 |
        //        //weight | The Pokémon’s weight | 51.84344482421875 |
        //        //form | The Pokémon’s form | 0 |
        //        //iv_attack | The Pokémon’s attack IV | 12 |
        //        //iv_defense | The Pokémon’s defense IV | 14 |
        //        //iv_stamina | The Pokémon’s stamina IV | 14 |
        //        //cp_multiplier | The Pokémon’s CP multiplier | 0.4785003960132599 |
        //        //additional_cp_multiplier | The Pokémon’s additional CP multiplier | 0.0 |
        //        //num_upgrades | The number of times that the Pokémon has been powered up | 31 |
        //        //deployment_time | The time at which the Pokémon was added to the gym | 1504361277 |

        //        Log($"Gym id: {id}");
        //        Log($"Name: {name}");
        //        Log($"Description: {description}");
        //        Log($"Url: {url}");
        //        Log($"Latitude: {latitude}");
        //        Log($"Longitude: {longitude}");
        //        Log($"Team: {team}");
        //        Log($"Pokemon:");

        //        foreach (var pkmn in pokemon)
        //        {
        //            string trainerName = Convert.ToString(pkmn["trainer_name"]);
        //            int trainerLevel = Convert.ToInt32(Convert.ToString(pkmn["trainer_level"]));
        //            string pokemonUid = Convert.ToString(pkmn["pokemon_uid"]);
        //            int pokemonId = Convert.ToInt32(Convert.ToString(pkmn["pokemon_id"]));
        //            int cp = Convert.ToInt32(Convert.ToString(pkmn["cp"]));
        //            int cpDecayed = Convert.ToInt32(Convert.ToString(pkmn["cp_decayed"]));
        //            int staminaMax = Convert.ToInt32(Convert.ToString(pkmn["stamina_max"]));
        //            int stamina = Convert.ToInt32(Convert.ToString(pkmn["stamina"]));
        //            string move1 = Convert.ToString(pkmn["move_1"] ?? "?");
        //            string move2 = Convert.ToString(pkmn["move_2"] ?? "?");
        //            double height = Convert.ToDouble(Convert.ToString(pkmn["height"]));
        //            double weight = Convert.ToDouble(Convert.ToString(pkmn["weight"]));
        //            int form = Convert.ToInt32(Convert.ToString(pkmn["form"]));
        //            int ivStamina = Convert.ToInt32(Convert.ToString(pkmn["iv_stamina"]));
        //            int ivAttack = Convert.ToInt32(Convert.ToString(pkmn["iv_attack"]));
        //            int ivDefense = Convert.ToInt32(Convert.ToString(pkmn["iv_defense"]));
        //            double cpMultiplier = Convert.ToDouble(Convert.ToString(pkmn["cp_multiplier"]));
        //            double additionalCpMultiplier = Convert.ToDouble(Convert.ToString(pkmn["additional_cp_multiplier"]));
        //            int numUpgrades = Convert.ToInt32(Convert.ToString(pkmn["num_upgrades"]));
        //            long deploymentTime = Convert.ToInt64(Convert.ToString(pkmn["deployment_time"]));

        //            Log($"Trainer Name: {trainerName}");
        //            Log($"Trainer Level: {trainerLevel}");
        //            Log($"Pokemon Uid: {pokemonUid}");
        //            Log($"Pokemon Id: {pokemonId}");
        //            Log($"CP: {cp}");
        //            Log($"CP Decayed: {cpDecayed}");
        //            Log($"Stamina: {stamina}");
        //            Log($"Stamina Max: {staminaMax}");
        //            Log($"Quick Move: {move1}");
        //            Log($"Charge Move: {move2}");
        //            Log($"Height: {height}");
        //            Log($"Width: {weight}");
        //            Log($"Form: {form}");
        //            Log($"IV Stamina: {ivStamina}");
        //            Log($"IV Attack: {ivAttack}");
        //            Log($"IV Defense: {ivDefense}");
        //            Log($"CP Multiplier: {cpMultiplier}");
        //            Log($"Additional CP Multiplier: {additionalCpMultiplier}");
        //            Log($"Number of Upgrades: {numUpgrades}");
        //            Log($"Deployment Time: {new DateTime(TimeSpan.FromSeconds(deploymentTime).Ticks)}");
        //            Log(Environment.NewLine);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Error(ex);
        //    }
        //}

        //private void ParseTth(dynamic message)
        //{
        //    try
        //    {
        //        //Field | Details | Example | | ————– | ———————————————————– | ————- |
        //        //name | The type of scheduler which performed the update | "SpeedScan" |
        //        //instance | The status name of scan instance which performed the update | "New York" |
        //        //tth_found | The completion status of the TTH scan | 0.9965 |
        //        //spawns_found | The number of spawns found in this update | 5 |
        //        string name = Convert.ToString(message["name"]);
        //        string instance = Convert.ToString(message["instance"]);
        //        double tthFound = Convert.ToDouble(Convert.ToString(message["tth_found"]));
        //        int spawnsFound = Convert.ToInt32(Convert.ToString(message["spawns_found"]));

        //        Log($"Name: {name}");
        //        Log($"Instance: {instance}");
        //        Log($"Tth Found: {tthFound}");
        //        Log($"Spawns Found: {spawnsFound}");
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Error(ex);
        //    }
        //}

        #endregion

        #region Private Methods

        private void Initialize()
        {
            try
            {
                var addresses = GetLocalIPv4Addresses(NetworkInterfaceType.Wireless80211);
                if (addresses.Count == 0)
                {
                    addresses = GetLocalIPv4Addresses(NetworkInterfaceType.Ethernet);
                }

                if (IsAdministrator())
                {
                    for (var i = 0; i < addresses.Count; i++)
                    {
                        var endpoint = PrepareEndPoint(addresses[i], Port);
                        if (!_server.Prefixes.Contains(endpoint))
                            _server.Prefixes.Add(endpoint);

                        _logger.Debug($"[IP ADDRESS] {endpoint}");
                    }
                }

                for (var i = 0; i < Strings.LocalEndPoint.Length; i++)
                {
                    var endpoint = PrepareEndPoint(Strings.LocalEndPoint[i], Port);
                    if (!_server.Prefixes.Contains(endpoint))
                        _server.Prefixes.Add(endpoint);

                    _logger.Debug($"[IP ADDRESS] {endpoint}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        #endregion

        #region Static Methods

        private static List<string> GetLocalIPv4Addresses(NetworkInterfaceType type)
        {
            var list = new List<string>();
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            for (int i = 0; i < interfaces.Length; i++)
            {
                var netInterface = interfaces[i];
                var isUp = netInterface.NetworkInterfaceType == type &&
                           netInterface.OperationalStatus == OperationalStatus.Up;
                if (!isUp) continue;

                foreach (var ipAddress in netInterface.GetIPProperties().UnicastAddresses)
                {
                    var isIpv4 = ipAddress.Address.AddressFamily == AddressFamily.InterNetwork;
                    if (isIpv4)
                    {
                        list.Add(ipAddress.Address.ToString());
                        break;
                    }
                }
            }
            return list;
        }

        private static string PrepareEndPoint(string ip, ushort port)
        {
            return $"http://{ip}:{port}/";
        }

        private static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        #endregion

        private class WebhookMessage
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("message")]
            public dynamic Message { get; set; }
        }
    }
}