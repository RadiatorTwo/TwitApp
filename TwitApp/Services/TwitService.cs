using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tweetinvi;
using Tweetinvi.Exceptions;
using Tweetinvi.Iterators;
using Tweetinvi.Parameters;
using TwitApp.Data;
using TwitApp.HostedServices;
using TwitApp.Models;

namespace TwitApp.Services
{
    public interface ITwitService
    {
        Task ApplyMigrations();
        Task LoadBlockedUsers();
        Task LoadFollower();
        Task LoadFriends();
        Task BlockUserAndFollower();
        Task BlockUsername(string usernameToBlock);
        Task UnblockUserAndFollower(string usernameToUnblock);
        Task BlockRecursive(string username, int maxDepth);

        Task<string> GetUsername(long id);

        Task<int> GetDbBlockedCount();
        Task<int> GetDbFollowerCount();
        Task<int> GetDbFriendCount();
    }

    public class TwitService : ITwitService
    {
#if DEBUG
        private static string blockedUsersFilePath = Path.Combine(@"C:\Progs\Twitblock", "blocked.txt");
        private static string inputBlockFilePath = Path.Combine(@"C:\Progs\Twitblock", "inputblock.txt");
#else
        private static string blockedUsersFilePath = Path.Combine(Environment.CurrentDirectory, "blocked.txt");
        private static string inputBlockFilePath = Path.Combine(Environment.CurrentDirectory, "inputblock.txt");
#endif

        private readonly TwitContext _twitContext;
        private readonly TwitterClient _twitterClient;

        public TwitService(TwitContext twitContext, TwitterClient twitterClient)
        {
            _twitContext = twitContext;
            _twitterClient = twitterClient;
        }

        public Task ApplyMigrations()
        {
            _twitContext.Database.Migrate();
            return Task.CompletedTask;
        }

        public async Task LoadBlockedUsers()
        {
            await _twitContext.Database.ExecuteSqlRawAsync("DELETE FROM BlockedUsers;");

            //var user = await _twitterClient.Users.GetAuthenticatedUserAsync();
            var parameter = new GetBlockedUserIdsParameters();
            parameter.PageSize = 5000;

            var iterator = _twitterClient.Users.GetBlockedUserIdsIterator(parameter);

            long i = 0;
            long count = 0;

            while (!iterator.Completed)
            {
                i++;
                AnsiConsole.MarkupLine("Page " + i.ToString());

                var queryRateLimit = await _twitterClient.RateLimits.GetRateLimitsAsync();

                if (queryRateLimit.BlocksIdsLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", queryRateLimit.BlocksIdsLimit.ResetDateTime);
                }

                var nextPage = await iterator.NextPageAsync();

                foreach (var blockedId in nextPage)
                {
                    var newBlockedUser = new BlockedUser();
                    newBlockedUser.ID = blockedId;

                    _twitContext.BlockedUsers.Add(newBlockedUser);
                    count++;
                }

                await _twitContext.SaveChangesAsync();
                Console.WriteLine("Count: {0}", count);

                await Task.Delay(500);
            }
        }

        public async Task LoadFollower()
        {
            await _twitContext.Database.ExecuteSqlRawAsync("DELETE FROM Follower;");

            var user = await _twitterClient.Users.GetAuthenticatedUserAsync();

            var parameter = new GetFollowerIdsParameters(user);
            parameter.PageSize = 5000;

            var iterator = _twitterClient.Users.GetFollowerIdsIterator(parameter);

            long i = 0;
            long count = 0;

            var queryRateLimit = await _twitterClient.RateLimits.GetRateLimitsAsync();

            while (!iterator.Completed)
            {
                i++;
                Console.WriteLine("Page " + i.ToString());


                if (queryRateLimit.FollowersIdsLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", queryRateLimit.FollowersIdsLimit.ResetDateTime);
                }

                var nextPage = await iterator.NextPageAsync();

                foreach (var followerId in nextPage)
                {
                    var newFollower = new Follower();
                    newFollower.ID = followerId;

                    _twitContext.Follower.Add(newFollower);
                    count++;
                }

                await _twitContext.SaveChangesAsync();
                Console.WriteLine("Count: {0}", count);
            }
        }

        public async Task LoadFriends()
        {
            await _twitContext.Database.ExecuteSqlRawAsync("DELETE FROM Friends;");

            var user = await _twitterClient.Users.GetAuthenticatedUserAsync();

            var parameter = new GetFriendIdsParameters(user);
            parameter.PageSize = 5000;

            var iterator = _twitterClient.Users.GetFriendIdsIterator(parameter);

            long i = 0;
            long count = 0;

            var queryRateLimit = await _twitterClient.RateLimits.GetRateLimitsAsync();

            while (!iterator.Completed)
            {
                i++;
                Console.WriteLine("Page " + i.ToString());

                if (queryRateLimit.FriendsIdsLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", queryRateLimit.FriendsIdsLimit.ResetDateTime);
                }

                var nextPage = await iterator.NextPageAsync();

                foreach (var followerId in nextPage)
                {
                    var newFriend = new Friend();
                    newFriend.ID = followerId;

                    _twitContext.Friends.Add(newFriend);
                    count++;
                }

                await _twitContext.SaveChangesAsync();
                Console.WriteLine("Count: {0}", count);
            }
        }

        public async Task BlockUserAndFollower()
        {
            var userList = File.ReadAllLines(inputBlockFilePath);

            foreach (var usernameToBlock in userList)
            {
                await BlockUsername(usernameToBlock);
            }
        }

        public async Task BlockUsername(string usernameToBlock)
        {
            Tweetinvi.Models.IUser userToBlock;

            try
            {
                userToBlock = await _twitterClient.Users.GetUserAsync(usernameToBlock);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

            Console.WriteLine("Blocke: {0}, {1}", userToBlock.Name, userToBlock.ScreenName);

            var followerToBlock = userToBlock.GetFollowerIds();

            var queryRateLimit = await _twitterClient.RateLimits.GetRateLimitsAsync();

            while (!followerToBlock.Completed)
            {
                ITwitterIteratorPage<long, string> followerPage;
                try
                {
                    if (queryRateLimit.FollowersIdsLimit.Remaining == 0)
                    {
                        Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", queryRateLimit.FollowersIdsLimit.ResetDateTime);
                    }

                    followerPage = await followerToBlock.NextPageAsync();
                }
                catch
                {
                    return;
                }

                foreach (var followerId in followerPage)
                {
                    if (await CheckBlockedId(followerId))
                    {
                        Console.WriteLine("Bereits geblockt: {0}", followerId);
                        continue;
                    }

                    if (await CheckFollowerId(followerId))
                    {
                        Console.WriteLine("Follower Skip: {0}", followerId.ToString());
                        continue;
                    }

                    if (await CheckFollowingId(followerId))
                    {
                        Console.WriteLine("Friend Skip: {0}", followerId.ToString());
                        continue;
                    }

                    try
                    {
                        await _twitterClient.Users.BlockUserAsync(followerId);
                        Console.WriteLine("Block durchgeführt: {0}", followerId.ToString());
                    }
                    catch (TwitterException ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    await AddBlockedId(followerId);
                    await Task.Delay(100);
                }
            }

            if (!await CheckBlockedId(userToBlock.Id))
            {
                await _twitterClient.Users.BlockUserAsync(userToBlock);
                await AddBlockedId(userToBlock.Id);
            }

            Console.WriteLine("Benutzer {0} und Follower geblockt.", userToBlock.Name);
        }

        public async Task UnblockUserAndFollower(string usernameToUnblock)
        {
            Tweetinvi.Models.IUser userToUnblock;

            try
            {
                userToUnblock = await _twitterClient.Users.GetUserAsync(usernameToUnblock);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

            AnsiConsole.MarkupLine("Entblocke: {0}, {1}", userToUnblock.Name, userToUnblock.ScreenName);

            if (await CheckBlockedId(userToUnblock.Id))
            {
                await _twitterClient.Users.UnblockUserAsync(userToUnblock);
                await RemoveBlockedId(userToUnblock.Id);
            }
            else
            {
                AnsiConsole.MarkupLine("Benutzer {0}, {1} ist nicht geblockt.", userToUnblock.Name, userToUnblock.ScreenName);
            }

            var followerToUnblock = userToUnblock.GetFollowerIds();

            var queryRateLimit = await _twitterClient.RateLimits.GetRateLimitsAsync();

            while (!followerToUnblock.Completed)
            {
                ITwitterIteratorPage<long, string> followerPage;
                try
                {
                    if (queryRateLimit.FollowersIdsLimit.Remaining == 0)
                    {
                        AnsiConsole.MarkupLine("Rate Limit erreicht. Fortsetzung: {0}", queryRateLimit.FollowersIdsLimit.ResetDateTime);
                    }

                    followerPage = await followerToUnblock.NextPageAsync();
                }
                catch
                {
                    return;
                }

                foreach (var followerId in followerPage)
                {
                    if (!await CheckBlockedId(followerId))
                    {
                        AnsiConsole.MarkupLine("Nicht geblockt: {0}", followerId);
                        continue;
                    }

                    try
                    {
                        await _twitterClient.Users.UnblockUserAsync(followerId);
                        AnsiConsole.MarkupLine("Unblock durchgeführt: {0}", followerId.ToString());
                    }
                    catch (TwitterException ex)
                    {
                        AnsiConsole.MarkupLine(ex.Message);
                    }

                    await RemoveBlockedId(followerId);
                    await Task.Delay(100);
                }
            }

            Console.WriteLine("Benutzer {0} und Follower entblockt.", userToUnblock.Name);
        }

        public async Task BlockRecursive(string username, int maxDepth)
        {
            Tweetinvi.Models.IUser user = null;

            try
            {
                Console.WriteLine("Lade User zum Blocken");
                user = await _twitterClient.Users.GetUserAsync(username);
            }
            catch
            {
            }

            if (user == null)
            {
                return;
            }

            Console.WriteLine("Blocke: {0}, {1}", user.Name, user.ScreenName);
            await _twitterClient.Users.BlockUserAsync(user.Id);

            Console.WriteLine("Lade Follower IDs");
            var ids = await GetFollowerIdsRecursive(user.Id, 0, maxDepth);
            Console.WriteLine("Anzahl geladener IDs: {0}", ids.Count);
            Console.WriteLine("Starte Block");

            await Task.Delay(1000);

            var queryRateLimit = await _twitterClient.RateLimits.GetRateLimitsAsync();

            foreach (var id in ids)
            {
                if (queryRateLimit.BlocksIdsLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", queryRateLimit.BlocksIdsLimit.ResetDateTime);
                }

                await _twitterClient.Users.BlockUserAsync(id);
            }
        }

        private async Task<List<long>> GetFollowerIdsRecursive(long id, int currentDepth, int maxDepth)
        {
            var result = new List<long>();
            if (currentDepth > maxDepth)
            {
                return result;
            }

            Tweetinvi.Models.IUser user = null;

            try
            {
                user = await _twitterClient.Users.GetUserAsync(id);
            }
            catch
            {
            }

            if (user == null)
            {
                return result;
            }

            var queryRateLimit = await _twitterClient.RateLimits.GetRateLimitsAsync();

            var iterator = user.GetFollowerIds();

            while (!iterator.Completed)
            {
                Console.WriteLine("Follower ID Remaining Rate Limit: {0}", queryRateLimit.FollowersIdsLimit.Remaining);
                if (queryRateLimit.FollowersIdsLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", queryRateLimit.FollowersIdsLimit.ResetDateTime);
                }

                var page = await iterator.NextPageAsync();

                foreach (var followerId in page)
                {
                    result.Add(followerId);

                    result.AddRange(await GetFollowerIdsRecursive(followerId, currentDepth + 1, maxDepth));
                }
            }

            return result;
        }

        private async Task<bool> CheckBlockedId(long id)
        {
            var result = await _twitContext.BlockedUsers.Where(user => user.ID == id).FirstOrDefaultAsync();
            return result != null;
        }

        private async Task<bool> CheckFollowerId(long id)
        {
            var result = await _twitContext.Follower.Where(user => user.ID == id).FirstOrDefaultAsync();
            return result != null;
        }

        private async Task<bool> CheckFollowingId(long id)
        {
            var result = await _twitContext.Friends.Where(user => user.ID == id).FirstOrDefaultAsync();
            return result != null;
        }

        private async Task AddBlockedId(long id)
        {
            var blockedUser = new BlockedUser();
            blockedUser.ID = id;

            _twitContext.BlockedUsers.Add(blockedUser);

            await _twitContext.SaveChangesAsync();
        }

        private async Task AddFollowerId(long id)
        {
            var blockedUser = new Follower();
            blockedUser.ID = id;

            _twitContext.Follower.Add(blockedUser);

            await _twitContext.SaveChangesAsync();
        }

        private async Task AddFollowingId(long id)
        {
            var blockedUser = new Friend();
            blockedUser.ID = id;

            _twitContext.Friends.Add(blockedUser);

            await _twitContext.SaveChangesAsync();
        }

        private async Task RemoveBlockedId(long id)
        {
            var blockedUser = await _twitContext.BlockedUsers.Where(user => user.ID == id).FirstOrDefaultAsync();
            if (blockedUser != null)
            {
                _twitContext.BlockedUsers.Remove(blockedUser);
                await _twitContext.SaveChangesAsync();
            }
        }

        public async Task<string> GetUsername(long id)
        {
            var user = await _twitterClient.Users.GetUserAsync(id);
            return user.UserDTO.ScreenName;
        }

        public async Task<int> GetDbBlockedCount()
        {
            return await _twitContext.BlockedUsers.CountAsync();
        }

        public async Task<int> GetDbFollowerCount()
        {
            return await _twitContext.Follower.CountAsync();
        }

        public async Task<int> GetDbFriendCount()
        {
            return await _twitContext.Friends.CountAsync();
        }
    }
}
