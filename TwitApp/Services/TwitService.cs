using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using TwitApp.Data;
using TwitApp.Models;
using CoreTweet;
using static TwitApp.Extensions.HelperExtensions;

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

        Task<RateLimit> GetBlocksRateLimit();
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
        private readonly Tokens _twitterClient;

        public TwitService(TwitContext twitContext, Tokens twitterClient)
        {
            _twitContext = twitContext;
            _twitterClient = twitterClient;
        }

        public Task ApplyMigrations()
        {
            _twitContext.Database.Migrate();
            return Task.CompletedTask;
        }

        private async Task CheckRateLimit(RateLimit rateLimit)
        {
            if (rateLimit.Remaining == 0)
            {
                AnsiConsole.MarkupLine("Rate Limit erreicht. Fortsetzung: {0}", rateLimit.Reset.LocalDateTime);

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.BouncingBar)
                    .StartAsync("Verbleibend: ", async ctx =>
                    {
                        while (DateTime.Now < rateLimit.Reset)
                        {
                            var span = rateLimit.Reset.LocalDateTime - DateTime.Now;
                            string formatted = string.Format("{0}{1}{2}{3}", span.Duration().Days > 0 ? string.Format("{0:0} Tag{1}, ", span.Days, span.Days == 1 ? string.Empty : "e") : string.Empty,
                                                                             span.Duration().Hours > 0 ? string.Format("{0:0} Stunden, ", span.Hours) : string.Empty,
                                                                             span.Duration().Minutes > 0 ? string.Format("{0:0} Minuten, ", span.Minutes) : string.Empty,
                                                                             span.Duration().Seconds > 0 ? string.Format("{0:0} Sekunden", span.Seconds) : string.Empty);

                            if (formatted.EndsWith(", ")) formatted = formatted.Substring(0, formatted.Length - 2);

                            if (string.IsNullOrEmpty(formatted)) formatted = "0 seconds";

                            ctx.Status = String.Format("Verbleibend: {0}", formatted);
                            await Task.Delay(1000);
                        }
                    });
            }
        }

        public async Task LoadBlockedUsers()
        {
            var rateLimit = await GetBlocksRateLimit();

            await CheckRateLimit(rateLimit);

            await _twitContext.Database.ExecuteSqlRawAsync("DELETE FROM BlockedUsers;");

            Cursored<long> iterator = null;

            try
            {
                iterator = await _twitterClient.Blocks.IdsAsync();
            }
            catch
            {
            }

            long i = 0;
            long count = 0;

            do
            {
                i++;
                await CheckRateLimit(iterator.RateLimit);

                AnsiConsole.MarkupLine("Page " + i.ToString());

                foreach (var blockedId in iterator.Result)
                {
                    var newBlockedUser = new BlockedUser();
                    newBlockedUser.ID = blockedId;

                    _twitContext.BlockedUsers.Add(newBlockedUser);
                    count++;
                }
                await _twitContext.SaveChangesAsync();

                if (iterator.NextCursor != 0)
                {
                    iterator = await _twitterClient.Blocks.IdsAsync(iterator.NextCursor);
                }

                AnsiConsole.MarkupLine("Count: {0}", count);
                await Task.Delay(100);
            } while (iterator.NextCursor != 0);
        }

        public async Task LoadFollower()
        {
            await _twitContext.Database.ExecuteSqlRawAsync("DELETE FROM Follower;");

            var iterator = await _twitterClient.Followers.IdsAsync();

            long i = 0;
            long count = 0;

            do
            {
                i++;
                Console.WriteLine("Page " + i.ToString());

                if (iterator.RateLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", iterator.RateLimit.Reset);
                }

                foreach (var followerId in iterator.Result)
                {
                    var newFollower = new Follower();
                    newFollower.ID = followerId;

                    _twitContext.Follower.Add(newFollower);
                    count++;
                }

                await _twitContext.SaveChangesAsync();

                if (iterator.NextCursor != 0)
                {
                    iterator = await _twitterClient.Followers.IdsAsync(iterator.NextCursor);
                }

                Console.WriteLine("Count: {0}", count);

                await Task.Delay(100);
            } while (iterator.NextCursor != 0);
        }

        public async Task LoadFriends()
        {
            await _twitContext.Database.ExecuteSqlRawAsync("DELETE FROM Friends;");

            var iterator = await _twitterClient.Friends.IdsAsync();

            long i = 0;
            long count = 0;

            do
            {
                i++;
                Console.WriteLine("Page " + i.ToString());

                if (iterator.RateLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", iterator.RateLimit.Reset);
                }

                foreach (var followerId in iterator.Result)
                {
                    var newFriend = new Friend();
                    newFriend.ID = followerId;

                    _twitContext.Friends.Add(newFriend);
                    count++;
                }

                await _twitContext.SaveChangesAsync();

                if (iterator.NextCursor != 0)
                {
                    iterator = await _twitterClient.Friends.IdsAsync(iterator.NextCursor);
                }

                Console.WriteLine("Count: {0}", count);
            } while (iterator.NextCursor != 0);
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
            UserResponse userToBlock;

            try
            {
                userToBlock = await _twitterClient.Users.ShowAsync(usernameToBlock);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

            Console.WriteLine("Blocke: {0}, {1}", userToBlock.Name, userToBlock.ScreenName);

            var iterator = await _twitterClient.Followers.IdsAsync(usernameToBlock);

            do
            {
                if (iterator.RateLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", iterator.RateLimit.Reset);
                }

                foreach (var followerId in iterator.Result)
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
                        await _twitterClient.Blocks.CreateAsync(followerId);
                        Console.WriteLine("Block durchgeführt: {0}", followerId.ToString());
                    }
                    catch (TwitterException ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    await AddBlockedId(followerId);
                    await Task.Delay(100);
                }
            } while (iterator.NextCursor != 0);

            if (!await CheckBlockedId((long)userToBlock.Id))
            {
                await _twitterClient.Blocks.CreateAsync(userToBlock.Id);
                await AddBlockedId((long)userToBlock.Id);
            }

            Console.WriteLine("Benutzer {0} und Follower geblockt.", userToBlock.Name);
        }

        public async Task UnblockUserAndFollower(string usernameToUnblock)
        {
            UserResponse userToUnblock;

            try
            {
                userToUnblock = await _twitterClient.Users.ShowAsync(usernameToUnblock);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

            AnsiConsole.MarkupLine("Entblocke: {0}, {1}", userToUnblock.Name, userToUnblock.ScreenName);

            if (await CheckBlockedId((long)userToUnblock.Id))
            {
                await _twitterClient.Blocks.DestroyAsync(userToUnblock);
                await RemoveBlockedId((long)userToUnblock.Id);
            }
            else
            {
                AnsiConsole.MarkupLine("Benutzer {0}, {1} ist nicht geblockt.", userToUnblock.Name, userToUnblock.ScreenName);
            }

            var iterator = await _twitterClient.Followers.IdsAsync(userToUnblock.Id);

            do
            {
                if (iterator.RateLimit.Remaining == 0)
                {
                    AnsiConsole.MarkupLine("Rate Limit erreicht. Fortsetzung: {0}", iterator.RateLimit.Reset);
                }

                foreach (var followerId in iterator.Result)
                {
                    if (!await CheckBlockedId(followerId))
                    {
                        AnsiConsole.MarkupLine("Nicht geblockt: {0}", followerId);
                        continue;
                    }

                    try
                    {
                        await _twitterClient.Blocks.DestroyAsync(followerId);
                        AnsiConsole.MarkupLine("Unblock durchgeführt: {0}", followerId);
                    }
                    catch (TwitterException ex)
                    {
                        AnsiConsole.MarkupLine(ex.Message);
                    }

                    await RemoveBlockedId(followerId);
                    await Task.Delay(100);
                }
            } while (iterator.NextCursor != 0);

            Console.WriteLine("Benutzer {0} und Follower entblockt.", userToUnblock.Name);
        }

        public async Task BlockRecursive(string username, int maxDepth)
        {
            UserResponse user = null;

            try
            {
                Console.WriteLine("Lade User zum Blocken");
                user = await _twitterClient.Users.ShowAsync(username);
            }
            catch
            {
            }

            if (user == null)
            {
                return;
            }

            Console.WriteLine("Blocke: {0}, {1}", user.Name, user.ScreenName);

            await _twitterClient.Blocks.CreateAsync(user.Id);

            Console.WriteLine("Lade Follower IDs");
            var ids = await GetFollowerIdsRecursive((long)user.Id, 0, maxDepth);
            Console.WriteLine("Anzahl geladener IDs: {0}", ids.Count);
            Console.WriteLine("Starte Block");

            await Task.Delay(500);

            foreach (var id in ids)
            {
                var response = await _twitterClient.Blocks.CreateAsync(id);
                if (response.RateLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", response.RateLimit.Reset);
                }
            }
        }

        private async Task<List<long>> GetFollowerIdsRecursive(long id, int currentDepth, int maxDepth)
        {
            var result = new List<long>();
            if (currentDepth > maxDepth)
            {
                return result;
            }

            UserResponse user = null;

            try
            {
                user = await _twitterClient.Users.ShowAsync(id);
            }
            catch
            {
            }

            if (user == null)
            {
                return result;
            }

            var iterator = await _twitterClient.Followers.IdsAsync(user.Id);

            do
            {
                Console.WriteLine("Follower ID Remaining Rate Limit: {0}", iterator.RateLimit.Remaining);
                if (iterator.RateLimit.Remaining == 0)
                {
                    Console.WriteLine("Rate Limit erreicht. Fortsetzung: {0}", iterator.RateLimit.Reset);
                }

                foreach (var followerId in iterator.Result)
                {
                    result.Add(followerId);
                    result.AddRange(await GetFollowerIdsRecursive(followerId, currentDepth + 1, maxDepth));
                }
            } while (iterator.NextCursor != 0);

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
            var user = await _twitterClient.Users.ShowAsync(id);
            return user.ScreenName;
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

        public async Task<RateLimit> GetBlocksRateLimit()
        {
            var rateLimits = await _twitterClient.Application.RateLimitStatusAsync(@"blocks");
            var rateLimit = rateLimits.Values.FirstOrDefault();

            return rateLimit["/blocks/ids"];
        }
    }
}
