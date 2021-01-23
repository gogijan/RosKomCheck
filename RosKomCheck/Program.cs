using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using FluentCloudflare;
using FluentCloudflare.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace RosKomCheck
{
    class Program
    {
        private static Logger Log { get; } = LogManager.GetCurrentClassLogger();
        private static List<string> IPs;
        private static List<string> Zones=new List<string>();
        static int Main(string[] args)
        {       
#if !DEBUG
            try
            {
#endif
            Check().Wait();


            var lookup = new LookupClient(IPAddress.Parse(ConfigurationManager.AppSettings["DNS1"]), IPAddress.Parse(ConfigurationManager.AppSettings["DNS2"]));
            foreach (var dom in Zones)
            {
                var record = lookup.Query(dom,QueryType.A,QueryClass.IN).Answers.Union(lookup.Query(dom,QueryType.AAAA,QueryClass.IN).Answers).OfType<AddressRecord>().Select(x=>x.Address.ToString()).ToList();
                if (record.Count == 0)
                {
                    Log.Info($"Домен {dom}: в DNS нет A/AAAA записей");
                }
                else
                {
                    foreach (var ip in record)
                    {
                        if (IPs.Contains(ip))
                            Log.Warn($"Домен {dom}: IP {ip} забанен РКН");
                    }

                    Log.Info($"Домен {dom}: проверено {record.Count} IP");
                }
            }
#if !DEBUG
            }
            catch (Exception e)
            {
                Log.Fatal(e);
                LogManager.Flush();
                LogManager.Shutdown();
                return 666;
            }
#endif
            LogManager.Flush();
            LogManager.Shutdown();
            return 0;
        }
        static async Task Check()
        {
                HttpClient client = new HttpClient();

                await Zone(ConfigurationManager.AppSettings["Token1"]);
                await Zone(ConfigurationManager.AppSettings["Token2"]);
                
                var response = await client.GetAsync("https://reestr.rublacklist.net/api/v3/ips-only/json/");
                if (!response.IsSuccessStatusCode)
                {
                    Log.Error($"{response.StatusCode} не получен список заблокированных IP");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                IPs = JsonConvert.DeserializeObject<List<string>>(responseContent);
                if (IPs.Count <100)
                {
                    Log.Error($"Мало забаненых IP");
                }
        }

        static async Task Zone(string t)
        {
            HttpClient client = new HttpClient();
            var cf = Cloudflare.WithToken(t);
            int z = 1;
            do
            {
                var zones = await cf.Zones.List().PerPage(50).Page(z)
                    .CallAsync(client);
                Zones.AddRange(zones.Select(x => x.Name));
                z++;
                if (zones.Count == 0)
                    z = 0;
            } while (z>0);
                
            if (Zones.Count == 0)
            {
                Log.Error($"В Cloudflare нет зон");
            }
        }
    }
}
