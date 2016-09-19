using RMMSharedModels;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace RMMDataService
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Single)]
    public class RagialPoller : IRagialPoller
    {
        private SortedSet<ItemInfo> m_ItemsToTrack = new SortedSet<ItemInfo>(new ItemInfoComparer());
        private object lock_itemSet = new object();

        private string m_TargetServer = "iRO-Renewal";
        private object lock_tgtServer = new object();

        private int m_DelayBetweenPollsInMS = 3000;
        private object lock_pollDelay = new object();


        public RagialPoller()
        {
            IServiceCallback clientCallback = OperationContext.Current.GetCallbackChannel<IServiceCallback>();
            new Thread(new ThreadStart(() => IterateItemSetAndRepeat(clientCallback))).Start();
        }

        public void RegisterObserver(string itemName)
        {
            lock (lock_itemSet)
            {
                //SortedSet makes this ridiculously easy...
                m_ItemsToTrack.Add(new ItemInfo(itemName));
            }
            //Adjust poll delay?
        }

        public void UnregisterObserver(string itemName)
        {
            //Remove from item set
            //Adjust poll delay?
        }

        public void SetTargetServer(string serverName)
        {
            lock (lock_tgtServer)
            {
                m_TargetServer = serverName;
            }
        }

        private async void IterateItemSetAndRepeat(IServiceCallback callback)
        {
            string currentItem = String.Empty;

            while (true)
            {
                //Determine which item is next to be polled
                lock (lock_itemSet)
                {
                    //This will get the next element in the set (alphabetical by Name), assuming there is one to get...
                    ItemInfo nextModel = m_ItemsToTrack.FirstOrDefault(model => String.Compare(model.Name, currentItem) > 0);
                    //...otherwise, circle back to the first item...
                    if (nextModel == null)
                    {
                        nextModel = m_ItemsToTrack.FirstOrDefault();
                    }
                    currentItem = nextModel != null ? nextModel.Name : String.Empty;
                }

                if (currentItem != String.Empty)
                {
                    ItemInfo updatedModel = null;
                    //Determine if cache contains "fresh" item info update
                    //  This is where we would query the DB for 'time_of_last_update'
                    //  Was it within the (TBD) time period
                    //
                    //if (cacheContainsFreshData)
                    //{
                    //    models[currentItem] = ... //query db, fill model
                    //}
                    //else
                    try
                    {
                        updatedModel = await PollDataSource(currentItem);
                    }
                    catch
                    {
                        try
                        {
                            callback.ClientErrorMessage("This is an error message!   >8-P  <3  :)  meh");
                        }
                        catch { }
                    }

                    if (updatedModel != null)
                    {
                        //Update memento
                        lock (lock_itemSet)
                        {
                            m_ItemsToTrack.RemoveWhere(item => item.Name.Equals(currentItem));
                            m_ItemsToTrack.Add(updatedModel);
                        }
                        //Push updated model to client
                        XmlSerializer xmlSerializer = new XmlSerializer(updatedModel.GetType());
                        MemoryStream itemInfoStream = new MemoryStream();
                        xmlSerializer.Serialize(itemInfoStream, updatedModel);
                        try
                        {
                            callback.ClientUpdate(itemInfoStream);
                        }
                        catch (TimeoutException)
                        {
                            //shutdown session?
                        }
                    }
                    Thread.Sleep(m_DelayBetweenPollsInMS);
                }
                else
                {
                    Thread.Sleep(1000);
                }
            }
        }

        async private Task<ItemInfo> PollDataSource(string itemName)
        {
            //This will be the return value, store all info in this object
            ItemInfo model = new ItemInfo(itemName);

            string pageSource = String.Empty;
            string getRequest = String.Empty;
            string serverName;
            lock (lock_tgtServer)
            {
                serverName = m_TargetServer;
            }

            using (HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) })
            {
                //Initial ragial.com GET request
                getRequest = String.Format("http://ragial.com/search/{0}/{1}", serverName, itemName.Replace(' ', '+'));
                pageSource = await httpClient.GETResponseHtmlString(getRequest);
                if (pageSource.Equals(String.Empty) || pageSource.ToUpper().Contains("NO ITEMS FOUND"))
                {
                    return null;
                }

                //Parse search Item ID and GET item-specific page
                string searchItemID = pageSource.Split(new string[] { serverName + "/" }, StringSplitOptions.None)
                                                .FirstOrDefault(substr => substr.ToUpper().Contains(("> " + itemName + "<").ToUpper()))
                                                .Split('"').FirstOrDefault();
                //Parse buyer entries
                getRequest = String.Format("http://ragial.com/item/{0}/{1}", serverName, searchItemID);
                pageSource = await httpClient.GETResponseHtmlString(getRequest);
                List<string> buyingNowRecords = pageSource.Split(new string[] { "<tr", "/tr" }, StringSplitOptions.None)
                                                            .Where(tr => tr.ToUpper().Contains("BUYING NOW"))
                                                            .ToList<string>();

                //Parse vendor entries
                getRequest = String.Format("http://ragial.com/item/{0}/{1}/1", serverName, searchItemID);
                pageSource = await httpClient.GETResponseHtmlString(getRequest);
                List<string> vendingNowRecords = pageSource.Split(new string[] { "<tr", "/tr>" }, StringSplitOptions.None)
                                            .Where(tr => tr.ToUpper().Contains("VENDING</TD>"))
                                            .ToList<string>();

                foreach (string vend in vendingNowRecords)
                {
                    try //If any one of these records has an unrecognized format, ignore it and try the next one
                    {
                        //GET request for shop specifics
                        string shopID = vend.RegexGrab(serverName + "/", "[^\"]+");
                        getRequest = String.Format("http://ragial.com/shop/{0}/{1}", serverName, shopID);
                        pageSource = await httpClient.GETResponseHtmlString(getRequest);

                        if (pageSource.Contains("account=" + shopID))
                        {
                            //Parse shop location
                            string locationSection = pageSource.Between("map_zone", "vi_tall");
                            string city = locationSection.RegexGrab("res/maps/", "[^_]+");
                            string coords = locationSection.RegexGrab(">", "(\\dF )?\\d+, \\d+");
                            
                            //Parse player name, last-seen, shop title
                            string playerSection = pageSource.Between("vi_tall", "</h2>");
                            string vendor = playerSection.RegexGrab("Name:</dd><dt>", "[^<]+").ConvertHtmlSymbols();
                            string lastSeenStr = playerSection.RegexGrab("Last Seen:</dd><dt>", "[^<]+");
                            DateTime lastSeen = DateTime.ParseExact(lastSeenStr.Replace(new string[] { "st,", "nd,", "rd,", "th," }, ","),
                                                                    "MMMM d, yyyy h:mmtt", CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces);
                            string title = playerSection.RegexGrab("/>", "[^<]+").ConvertHtmlSymbols();

                            //Parse quantity, price, and variance
                            uint qty = uint.Parse(vend.RegexGrab("class=\"amt\">", "[^x]+"), NumberStyles.AllowThousands);
                            uint price = uint.Parse(vend.RegexGrab("rel=\"notip\">", "\\d+(,\\d{3})*"), NumberStyles.AllowThousands);
                            string stdCh = pageSource.Split(new string[] { "/>" }, StringSplitOptions.RemoveEmptyEntries)
                                                        .FirstOrDefault(subStr => subStr.ToUpper().Contains(itemName.ToUpper() + "<TD CLASS=\"AMT\""));
                            int variance = int.Parse(stdCh.RegexGrab("std ..\">", "[^%]+"));

                            //Build collection of vending shopInfos
                            model.Vendors.Add(new ShopInfo
                            {
                                ShopType = ShopInfo.TransactionRole.Vendor,
                                Item = itemName,
                                PlayerName = vendor,
                                Title = title,
                                Map = city,
                                Coords = coords,
                                Qty = qty,
                                Price = price,
                                Variance = variance,
                                LastSeen = lastSeen
                            });
                        }
                    }
                    catch { } //Unable to parse shop info for this vendor... try the next one
                }

                foreach (string buy in buyingNowRecords)
                {
                    try //If any one of these records has an unrecognized format, ignore it and try the next one
                    {
                        //GET request for shop specifics
                        string shopID = buy.RegexGrab(serverName + "/", "[^\"]+");
                        getRequest = String.Format("http://ragial.com/shop/{0}/{1}", serverName, shopID);
                        pageSource = await httpClient.GETResponseHtmlString(getRequest);

                        if (pageSource.Contains("account=" + shopID))
                        {
                            //Parse shop location
                            string locationSection = pageSource.Between("map_zone", "vi_tall");
                            string city = locationSection.RegexGrab("res/maps/", "[^_]+");
                            string coords = locationSection.RegexGrab(">", "(\\dF )?\\d+, \\d+");

                            //Parse player name, last-seen, shop title
                            string playerSection = pageSource.Between("vi_tall", "</h2>");
                            string buyer = playerSection.RegexGrab("Name:</dd><dt>", "[^<]+").ConvertHtmlSymbols();
                            string lastSeenStr = playerSection.RegexGrab("Last Seen:</dd><dt>", "[^<]+");
                            DateTime lastSeen = DateTime.ParseExact(lastSeenStr.Replace(new string[] { "st,", "nd,", "rd,", "th," }, ","),
                                                                    "MMMM d, yyyy h:mmtt", CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces);
                            string title = playerSection.RegexGrab("/>", "[^<]+").ConvertHtmlSymbols();

                            //Parse quantity, price, and variance
                            uint qty = uint.Parse(buy.RegexGrab("id=\"amt\">", "[^x]+"), NumberStyles.AllowThousands);
                            uint price = uint.Parse(buy.RegexGrab("rel=\"notip\">", "\\d+(,\\d{3})*"), NumberStyles.AllowThousands);
                            string[] stdChArray = pageSource.Split(new string[] { "/>" }, StringSplitOptions.RemoveEmptyEntries);
                            string stdCh = stdChArray.FirstOrDefault(subStr => subStr.ToUpper().Contains(itemName.ToUpper() + "<TD CLASS=\"AMT\""));
                            int variance = int.Parse(stdCh.RegexGrab("std ..\">", "[^%]+"));

                            //Build collection of vending shopInfos
                            model.Buyers.Add(new ShopInfo
                            {
                                ShopType = ShopInfo.TransactionRole.Buyer,
                                Item = itemName,
                                PlayerName = buyer,
                                Title = title,
                                Map = city,
                                Coords = coords,
                                Qty = qty,
                                Price = price,
                                Variance = variance,
                                LastSeen = lastSeen
                            });
                        }
                    }
                    catch { } //Unable to parse shop info for this buyer... try the next one
                }

                //Look up NPC buy price from that other website
                model.NPCBuyPrice = 1234567;

            }//end Using <HttpClient>
            return model;
        }
    }
}
