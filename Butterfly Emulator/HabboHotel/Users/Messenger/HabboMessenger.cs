﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Butterfly.Collections;
using Butterfly.HabboHotel.GameClients;
using Butterfly.HabboHotel.Rooms;
using Butterfly.Messages;
using Butterfly.HabboHotel.Users.Messenger;
using Database_Manager.Database.Session_Details.Interfaces;

namespace Butterfly.HabboHotel.Users.Messenger
{
    class HabboMessenger
    {
        private uint UserId;
        private Dictionary<uint, MessengerRequest> requests;
        private Dictionary<uint, MessengerBuddy> friends;

        internal bool AppearOffline;

        internal HabboMessenger(uint UserId)
        {
            this.requests = new Dictionary<uint, MessengerRequest>();
            this.friends = new Dictionary<uint, MessengerBuddy>();
            this.UserId = UserId;
        }

        internal void Init(Dictionary<uint, MessengerBuddy> friends, Dictionary<uint, MessengerRequest> requests)
        {
            this.requests = new Dictionary<uint, MessengerRequest>(requests);
            this.friends = new Dictionary<uint, MessengerBuddy>(friends);
        }

        internal void ClearRequests()
        {
            requests.Clear();
        }

        internal MessengerRequest GetRequest(uint senderID)
        {
            if (requests.ContainsKey(senderID))
                return requests[senderID];

            return null;
        }

        internal void Destroy()
        {
            IEnumerable<GameClient> onlineUsers = ButterflyEnvironment.GetGame().GetClientManager().GetClientsById(friends.Keys);

            foreach (GameClient client in onlineUsers)
            {
                if (client.GetHabbo() == null || client.GetHabbo().GetMessenger() == null)
                    continue;

                client.GetHabbo().GetMessenger().UpdateFriend(this.UserId, null, true);
            }
        }

        internal void OnStatusChanged(bool notification)
        {
            IEnumerable<GameClient> onlineUsers = ButterflyEnvironment.GetGame().GetClientManager().GetClientsById(friends.Keys);

            foreach (GameClient client in onlineUsers)
            {
                if (client == null || client.GetHabbo() == null || client.GetHabbo().GetMessenger() == null)
                    continue;

                client.GetHabbo().GetMessenger().UpdateFriend(this.UserId, client, true);
                UpdateFriend(client.GetHabbo().Id, client, notification);
            }
        }

        internal void UpdateFriend(uint userid, GameClient client, bool notification)
        {
            if (friends.ContainsKey(userid))
            {
                friends[userid].UpdateUser(client);

                if (notification)
                {
                    GameClient Userclient = GetClient();
                    if (Userclient != null)
                        Userclient.SendMessage(SerializeUpdate((MessengerBuddy)friends[userid]));
                }
            }
        }

        internal void HandleAllRequests()
        {
            using (IQueryAdapter dbClient = ButterflyEnvironment.GetDatabaseManager().getQueryreactor())
            {
                dbClient.runFastQuery("DELETE FROM messenger_requests WHERE sender = " + UserId + " OR receiver = " + UserId);
            }

            ClearRequests();
        }

        internal void HandleRequest(uint sender)
        {
            using (IQueryAdapter dbClient = ButterflyEnvironment.GetDatabaseManager().getQueryreactor())
            {
                dbClient.runFastQuery("DELETE FROM messenger_requests WHERE (sender = " + UserId + " AND receiver = " + sender + ") OR (receiver = " + UserId + " AND sender = " + sender + ")");
            }

            requests.Remove(sender);
        }

        internal void CreateFriendship(uint friendID)
        {
            using (IQueryAdapter dbClient = ButterflyEnvironment.GetDatabaseManager().getQueryreactor())
            {
                if (dbClient.dbType == Database_Manager.Database.DatabaseType.MSSQL)
                {
                    dbClient.runFastQuery("DELETE FROM messenger_friendships WHERE sender = " + UserId + " AND receiver = " + friendID);
                    dbClient.runFastQuery("INSERT INTO messenger_friendships (sender,receiver) VALUES (" + UserId + "," + friendID + ")");
                }
                else
                {
                    dbClient.runFastQuery("REPLACE INTO messenger_friendships (sender,receiver) VALUES (" + UserId + "," + friendID + ")");
                }
            }

            OnNewFriendship(friendID);

            GameClient User = ButterflyEnvironment.GetGame().GetClientManager().GetClientByUserID(friendID);

            if (User != null && User.GetHabbo().GetMessenger() != null)
                User.GetHabbo().GetMessenger().OnNewFriendship(UserId);
        }

        internal void DestroyFriendship(uint friendID)
        {
            using (IQueryAdapter dbClient = ButterflyEnvironment.GetDatabaseManager().getQueryreactor())
            {
                dbClient.runFastQuery("DELETE FROM messenger_friendships WHERE (sender = " + UserId + " AND receiver = " + friendID + ") OR (receiver = " + UserId + " AND sender = " + friendID + ")");
            }

            OnDestroyFriendship(friendID);

            GameClient User = ButterflyEnvironment.GetGame().GetClientManager().GetClientByUserID(friendID);

            if (User != null && User.GetHabbo().GetMessenger() != null)
                User.GetHabbo().GetMessenger().OnDestroyFriendship(UserId);
        }

        internal void OnNewFriendship(uint friendID)
        {
            GameClient friend = ButterflyEnvironment.GetGame().GetClientManager().GetClientByUserID(friendID);

            MessengerBuddy newFriend;
            if (friend == null || friend.GetHabbo() == null)
            {
                DataRow dRow;
                using (IQueryAdapter dbClient = ButterflyEnvironment.GetDatabaseManager().getQueryreactor())
                {
                    dbClient.setQuery("SELECT username,motto,look,last_online FROM users WHERE id = " + friendID);
                    dRow = dbClient.getRow();
                }

                newFriend = new MessengerBuddy(friendID, (string)dRow["username"], (string)dRow["look"], (string)dRow["motto"], (string)dRow["last_online"]);
            }
            else
            {
                Habbo user = friend.GetHabbo();
                newFriend = new MessengerBuddy(friendID, user.Username, user.Look, user.Motto, string.Empty);
                newFriend.UpdateUser(friend);
            }

            if (!friends.ContainsKey(friendID))
                friends.Add(friendID, newFriend);

            GetClient().SendMessage(SerializeUpdate(newFriend));
        }

        internal bool RequestExists(uint requestID)
        {
            if (requests.ContainsKey(requestID))
                return true;

            using (IQueryAdapter dbClient = ButterflyEnvironment.GetDatabaseManager().getQueryreactor())
            {
                dbClient.setQuery("SELECT sender FROM messenger_friendships WHERE sender = @myID AND receiver = @friendID");
                dbClient.addParameter("myID", (int)this.UserId);
                dbClient.addParameter("friendID", (int)requestID);
                return dbClient.findsResult();
            }

        }

        internal bool FriendshipExists(uint friendID)
        {
            return friends.ContainsKey(friendID);
        }

        internal void OnDestroyFriendship(uint Friend)
        {
            friends.Remove(Friend);

            GetClient().GetMessageHandler().GetResponse().Init(13);
            GetClient().GetMessageHandler().GetResponse().AppendInt32(0);
            GetClient().GetMessageHandler().GetResponse().AppendInt32(1);
            GetClient().GetMessageHandler().GetResponse().AppendInt32(-1);
            GetClient().GetMessageHandler().GetResponse().AppendUInt(Friend);
            GetClient().GetMessageHandler().SendResponse();
        }

        internal bool RequestBuddy(string UserQuery)
        {
            uint userID;
            bool hasFQDisabled;

            GameClient client = ButterflyEnvironment.GetGame().GetClientManager().GetClientByUsername(UserQuery);

            if (client == null)
            {
                DataRow Row = null;
                using (IQueryAdapter dbClient = ButterflyEnvironment.GetDatabaseManager().getQueryreactor())
                {
                    dbClient.setQuery("SELECT id,block_newfriends FROM users WHERE username = @query");
                    dbClient.addParameter("query", UserQuery.ToLower());
                    Row = dbClient.getRow();
                }

                if (Row == null)
                    return false;

                userID = Convert.ToUInt32(Row["id"]);
                hasFQDisabled = ButterflyEnvironment.EnumToBool(Row["block_newfriends"].ToString());
            }
            else
            {
                userID = client.GetHabbo().Id;
                hasFQDisabled = client.GetHabbo().HasFriendRequestsDisabled;
            }

            if (hasFQDisabled)
            {
                GetClient().GetMessageHandler().GetResponse().Init(260);
                GetClient().GetMessageHandler().GetResponse().AppendInt32(39);
                GetClient().GetMessageHandler().GetResponse().AppendInt32(3);
                GetClient().GetMessageHandler().SendResponse();
                return true;
            }

            uint ToId = userID;

            if (RequestExists(ToId))
            {
                return true;
            }

            using (IQueryAdapter dbClient = ButterflyEnvironment.GetDatabaseManager().getQueryreactor())
            {
                if (dbClient.dbType == Database_Manager.Database.DatabaseType.MSSQL)
                {
                    dbClient.runFastQuery("DELETE FROM messenger_requests WHERE sender = " + userID + " AND receiver = " + ToId);
                    dbClient.runFastQuery("INSERT INTO messenger_requests (sender,receiver) VALUES (" + this.UserId + "," + ToId + ")");
                }
                else
                {
                    dbClient.runFastQuery("REPLACE INTO messenger_requests (sender,receiver) VALUES (" + this.UserId + "," + ToId + ")");
                }
            }

            GameClient ToUser = ButterflyEnvironment.GetGame().GetClientManager().GetClientByUserID(ToId);

            if (ToUser == null || ToUser.GetHabbo() == null)
            {
                return true;
            }

            MessengerRequest Request = new MessengerRequest(ToId, UserId, ButterflyEnvironment.GetGame().GetClientManager().GetNameById(UserId));

            ToUser.GetHabbo().GetMessenger().OnNewRequest(UserId);

            ServerMessage NewFriendNotif = new ServerMessage(132);
            Request.Serialize(NewFriendNotif);
            ToUser.SendMessage(NewFriendNotif);
            requests.Add(ToId, Request);
            return true;
        }

        internal void OnNewRequest(uint friendID)
        {
            if (!requests.ContainsKey(friendID))
                requests.Add(friendID, new MessengerRequest(UserId, friendID, ButterflyEnvironment.GetGame().GetClientManager().GetNameById(friendID)));
        }

        internal void SendInstantMessage(UInt32 ToId, string Message)
        {
            if (!FriendshipExists(ToId))
            {
                DeliverInstantMessageError(6, ToId);
                return;
            }

            GameClient Client = ButterflyEnvironment.GetGame().GetClientManager().GetClientByUserID(ToId);

            if (Client == null || Client.GetHabbo().GetMessenger() == null)
            {
                DeliverInstantMessageError(5, ToId);
                return;
            }

            if (GetClient().GetHabbo().Muted)
            {
                DeliverInstantMessageError(4, ToId);
                return;
            }

            if (Client.GetHabbo().Muted)
            {
                DeliverInstantMessageError(3, ToId); // No return, as this is just a warning.
            }

            Client.GetHabbo().GetMessenger().DeliverInstantMessage(Message, UserId);
        }

        internal void DeliverInstantMessage(string message, uint convoID)
        {
            ServerMessage InstantMessage = new ServerMessage(134);
            InstantMessage.AppendUInt(convoID);
            InstantMessage.AppendString(message);
            GetClient().SendMessage(InstantMessage);
        }

        internal void DeliverInstantMessageError(int ErrorId, UInt32 ConversationId)
        {
            /*
3                =     Your friend is muted and cannot reply.
4                =     Your message was not sent because you are muted.
5                =     Your friend is not online.
6                =     Receiver is not your friend anymore.
7                =     Your friend is busy.
8                =     Your friend is wanking*/

            ServerMessage reply = new ServerMessage(261);
            reply.AppendInt32(ErrorId);
            reply.AppendUInt(ConversationId);
            GetClient().SendMessage(reply);
        }

        internal ServerMessage SerializeFriends()
        {
            ServerMessage reply = new ServerMessage(12);
            reply.AppendInt32(600);
            reply.AppendInt32(200);
            reply.AppendInt32(600);
            reply.AppendInt32(900);
            reply.AppendBoolean(false);
            reply.AppendInt32(friends.Count);

            foreach (MessengerBuddy friend in friends.Values)
            {
                friend.Serialize(reply);
            }

            return reply;
        }

        internal static ServerMessage SerializeUpdate(MessengerBuddy friend)
        {
            ServerMessage reply = new ServerMessage(13);
            reply.AppendInt32(0);
            reply.AppendInt32(1);
            reply.AppendInt32(0);

            friend.Serialize(reply);
            reply.AppendBoolean(false);

            return reply;
        }

        internal ServerMessage SerializeRequests()
        {
            ServerMessage reply = new ServerMessage(314);

            reply.AppendInt32(requests.Count);
            reply.AppendInt32(requests.Count);

            foreach (MessengerRequest request in requests.Values)
            {
                request.Serialize(reply);
            }

            return reply;
        }

        internal ServerMessage PerformSearch(string query)
        {
            List<SearchResult> results = SearchResultFactory.GetSearchResult(query);

            List<SearchResult> existingFriends = new List<SearchResult>();
            List<SearchResult> othersUsers = new List<SearchResult>();

            foreach (SearchResult result in results)
            {
                if (FriendshipExists(result.userID))
                    existingFriends.Add(result);
                else
                    othersUsers.Add(result);
            }
     
            ServerMessage reply = new ServerMessage(435);

            reply.AppendInt32(existingFriends.Count);
            foreach (SearchResult result in existingFriends)
            {
                result.Searialize(reply);
            }

            reply.AppendInt32(othersUsers.Count);
            foreach (SearchResult result in othersUsers)
            {
                result.Searialize(reply);
            }

            return reply;
        }

        private GameClient GetClient()
        {
            return ButterflyEnvironment.GetGame().GetClientManager().GetClientByUserID(UserId);
        }

        internal IEnumerable<RoomData> GetActiveFriendsRooms()
        {
            foreach (MessengerBuddy buddy in friends.Values.Where(p => p.InRoom))
                yield return buddy.CurrentRoom.RoomData;
        }
    }
}