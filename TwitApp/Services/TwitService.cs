using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using TwitApp.Data;
using TwitApp.Models;
using CoreTweet;
using CoreTweet.Rest;
using CoreTweet.Core;
using Status = CoreTweet.Status;

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

        Task FollowRetweets(long tweetid);
        Task FollowFollower();

        Task<string> GetUsername(long id);

        Task<int> GetDbBlockedCount();
        Task<int> GetDbFollowerCount();
        Task<int> GetDbFriendCount();

        Task AddBlock(long id);
        Task AddFollower(long id);
        Task AddFriend(long id);

        Task<Cursor> GetCursor(string cursorName);

        Task<RateLimit> GetBlocksRateLimit();
        Task<RateLimit> GetFollowerRateLimit();
        Task<RateLimit> GetFriendsRateLimit();
    }

    public class TwitService : ITwitService
    {
#if DEBUG
        private static string blockedUsersFilePath = Path.Combine(@"C:\Progs\Twitblock", "blocked.txt");
        private static string inputBlockFilePath = Path.Combine(@"C:\Progs\Twitblock", "inputblock.txt");
#else
        //private static string blockedUsersFilePath = Path.Combine(Environment.CurrentDirectory, "blocked.txt");
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

        private async Task CheckRateLimit(RateLimit rateLimit, StatusContext statusContext)
        {
            if (rateLimit.Remaining == 0)
            {
                AnsiConsole.MarkupLine("Rate Limit erreicht. Fortsetzung: {0}", rateLimit.Reset.LocalDateTime);
                statusContext.Spinner = Spinner.Known.BouncingBar;

                while (DateTime.Now < rateLimit.Reset)
                {
                    var span = rateLimit.Reset.LocalDateTime - DateTime.Now;
                    string formatted = string.Format("{0}{1}{2}{3}", span.Duration().Days > 0 ? string.Format("{0:0} Tag{1}, ", span.Days, span.Days == 1 ? string.Empty : "e") : string.Empty,
                                                                     span.Duration().Hours > 0 ? string.Format("{0:0} Stunden, ", span.Hours) : string.Empty,
                                                                     span.Duration().Minutes > 0 ? string.Format("{0:0} Minuten, ", span.Minutes) : string.Empty,
                                                                     span.Duration().Seconds > 0 ? string.Format("{0:0} Sekunden", span.Seconds) : string.Empty);

                    if (formatted.EndsWith(", ")) formatted = formatted.Substring(0, formatted.Length - 2);

                    if (string.IsNullOrEmpty(formatted)) formatted = "0 seconds";

                    statusContext.Status = String.Format("Verbleibend: {0}", formatted);
                    await Task.Delay(1000);
                }

                await Task.Delay(2000);
            }
        }

        public async Task LoadBlockedUsers()
        {
            await AnsiConsole.Status()
                    .StartAsync("Lade geblockte User", async ctx =>
                    {
                        ctx.Status = "Checke Rate Limit";
                        var rateLimit = await GetBlocksRateLimit();

                        await CheckRateLimit(rateLimit, ctx);

                        ctx.Spinner = Spinner.Known.Dots12;

                        //await _twitContext.Database.ExecuteSqlRawAsync("DELETE FROM BlockedUsers;");

                        Cursored<long> iterator = null;
                        var cursor = await GetCursor("blocks");

                        ctx.Status = "Lade Twitter IDs";

                        try
                        {
                            if (cursor != null)
                            {
                                iterator = await _twitterClient.Blocks.IdsAsync(cursor.CursorID);
                            }
                            else
                            {
                                iterator = await _twitterClient.Blocks.IdsAsync();
                            }
                        }
                        catch
                        {
                        }

                        var count = await GetDbBlockedCount();

                        do
                        {
                            ctx.Status = "Speichere Twitter IDs in Datenbank";

                            foreach (var blockedId in iterator.Result)
                            {
                                count++;
                                await AddBlock(blockedId);
                            }

                            await _twitContext.SaveChangesAsync();

                            AnsiConsole.MarkupLine("Gesamtanzahl Blocks: [green]{0}[/]", count);

                            if (cursor != null)
                            {
                                cursor.CursorID = iterator.NextCursor;
                                _twitContext.Cursors.Update(cursor);
                            }
                            else
                            {
                                cursor = new Cursor();
                                cursor.CursorID = iterator.NextCursor;
                                cursor.Name = "blocks";
                                _twitContext.Cursors.Add(cursor);
                            }

                            await _twitContext.SaveChangesAsync();

                            if (iterator.NextCursor != 0)
                            {
                                ctx.Status = "Lade Twitter IDs";

                                if (iterator.RateLimit == null)
                                {
                                    rateLimit = await GetBlocksRateLimit();
                                    await CheckRateLimit(rateLimit, ctx);
                                }
                                else
                                {
                                    await CheckRateLimit(iterator.RateLimit, ctx);
                                }

                                iterator = await _twitterClient.Blocks.IdsAsync(iterator.NextCursor);
                            }
                            else
                            {
                                cursor = await _twitContext.Cursors.Where(cursor => cursor.Name == "blocks").FirstOrDefaultAsync();
                                if (cursor != null)
                                {
                                    _twitContext.Cursors.Remove(cursor); ;
                                    await _twitContext.SaveChangesAsync();
                                }
                            }

                            await Task.Delay(100);
                        } while (iterator.NextCursor != 0);
                    });
        }

        public async Task LoadFollower()
        {
            await AnsiConsole.Status()
                    .StartAsync("Lade Follower", async ctx =>
                    {
                        ctx.Status = "Checke Rate Limit";
                        var rateLimit = await GetFollowerRateLimit();

                        await CheckRateLimit(rateLimit, ctx);

                        ctx.Spinner = Spinner.Known.Dots12;

                        Cursored<long> iterator = null;
                        var cursor = await GetCursor("followers");

                        ctx.Status = "Lade Twitter IDs";

                        try
                        {
                            if (cursor != null)
                            {
                                iterator = await _twitterClient.Followers.IdsAsync(cursor.CursorID);
                            }
                            else
                            {
                                iterator = await _twitterClient.Followers.IdsAsync();
                            }
                        }
                        catch
                        {
                        }

                        var count = await GetDbFollowerCount();

                        do
                        {
                            ctx.Status = "Speichere Twitter IDs in Datenbank";

                            foreach (var followerId in iterator.Result)
                            {
                                count++;
                                await AddFollower(followerId);
                            }

                            await _twitContext.SaveChangesAsync();

                            AnsiConsole.MarkupLine("Gesamtanzahl Follower: [green]{0}[/]", count);

                            if (cursor != null)
                            {
                                cursor.CursorID = iterator.NextCursor;
                                _twitContext.Cursors.Update(cursor);
                            }
                            else
                            {
                                cursor = new Cursor();
                                cursor.CursorID = iterator.NextCursor;
                                cursor.Name = "followers";
                                _twitContext.Cursors.Add(cursor);
                            }

                            await _twitContext.SaveChangesAsync();

                            if (iterator.NextCursor != 0)
                            {
                                ctx.Status = "Lade Twitter IDs";

                                if (iterator.RateLimit == null)
                                {
                                    rateLimit = await GetFollowerRateLimit();
                                    await CheckRateLimit(rateLimit, ctx);
                                }
                                else
                                {
                                    await CheckRateLimit(iterator.RateLimit, ctx);
                                }

                                iterator = await _twitterClient.Followers.IdsAsync(iterator.NextCursor);
                            }
                            else
                            {
                                cursor = await _twitContext.Cursors.Where(cursor => cursor.Name == "followers").FirstOrDefaultAsync();
                                if (cursor != null)
                                {
                                    _twitContext.Cursors.Remove(cursor);
                                    await _twitContext.SaveChangesAsync();
                                }
                            }

                            await Task.Delay(100);
                        } while (iterator.NextCursor != 0);
                    });
        }

        public async Task LoadFriends()
        {
            await AnsiConsole.Status()
                    .StartAsync("Lade Freunde", async ctx =>
                    {
                        ctx.Status = "Checke Rate Limit";
                        var rateLimit = await GetFriendsRateLimit();

                        await CheckRateLimit(rateLimit, ctx);

                        ctx.Spinner = Spinner.Known.Dots12;

                        //await _twitContext.Database.ExecuteSqlRawAsync("DELETE FROM BlockedUsers;");

                        Cursored<long> iterator = null;
                        var cursor = await GetCursor("friends");

                        ctx.Status = "Lade Twitter IDs";

                        try
                        {
                            if (cursor != null)
                            {
                                iterator = await _twitterClient.Friends.IdsAsync(cursor.CursorID);
                            }
                            else
                            {
                                iterator = await _twitterClient.Friends.IdsAsync();
                            }
                        }
                        catch
                        {
                        }

                        var count = await GetDbFriendCount();

                        do
                        {
                            ctx.Status = "Speichere Twitter IDs in Datenbank";

                            foreach (var friendId in iterator.Result)
                            {
                                count++;
                                await AddFriend(friendId);
                            }

                            await _twitContext.SaveChangesAsync();

                            AnsiConsole.MarkupLine("Gesamtanzahl Freunde: [green]{0}[/]", count);

                            if (cursor != null)
                            {
                                cursor.CursorID = iterator.NextCursor;
                                _twitContext.Cursors.Update(cursor);
                            }
                            else
                            {
                                cursor = new Cursor();
                                cursor.CursorID = iterator.NextCursor;
                                cursor.Name = "friends";
                                _twitContext.Cursors.Add(cursor);
                            }

                            await _twitContext.SaveChangesAsync();

                            if (iterator.NextCursor != 0)
                            {
                                ctx.Status = "Lade Twitter IDs";

                                if (iterator.RateLimit == null)
                                {
                                    rateLimit = await GetFriendsRateLimit();
                                    await CheckRateLimit(rateLimit, ctx);
                                }
                                else
                                {
                                    await CheckRateLimit(iterator.RateLimit, ctx);
                                }

                                iterator = await _twitterClient.Friends.IdsAsync(iterator.NextCursor);
                            }
                            else
                            {
                                cursor = await _twitContext.Cursors.Where(cursor => cursor.Name == "friends").FirstOrDefaultAsync();
                                if (cursor != null)
                                {
                                    _twitContext.Cursors.Remove(cursor); ;
                                    await _twitContext.SaveChangesAsync();
                                }
                            }

                            await Task.Delay(100);
                        } while (iterator.NextCursor != 0);
                    });
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
            await AnsiConsole.Status()
                    .StartAsync("Lade Zu Blockenden User", async ctx =>
                    {
                        UserResponse userToBlock;

                        try
                        {
                            userToBlock = await _twitterClient.Users.ShowAsync(usernameToBlock);
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine(ex.Message);
                            return;
                        }
                        ctx.Status = "Checke Rate Limit";

                        var rateLimit = await GetFollowerRateLimit();

                        await CheckRateLimit(rateLimit, ctx);

                        ctx.Spinner = Spinner.Known.Dots12;

                        AnsiConsole.MarkupLine("Blocke: {0}, {1}", userToBlock.Name, userToBlock.ScreenName);

                        ctx.Status = "Lade Follower IDs";
                        var iterator = await _twitterClient.Followers.IdsAsync(usernameToBlock);

                        do
                        {
                            ctx.Status = $"Blocke Follower von {userToBlock.ScreenName} | {userToBlock.Name}";

                            foreach (var followerId in iterator.Result)
                            {
                                if (await CheckBlockedId(followerId))
                                {
                                    AnsiConsole.MarkupLine("Bereits geblockt: {0}", followerId);
                                    continue;
                                }

                                if (await CheckFollowerId(followerId))
                                {
                                    AnsiConsole.MarkupLine("Follower Skip: {0}", followerId.ToString());
                                    continue;
                                }

                                if (await CheckFollowingId(followerId))
                                {
                                    AnsiConsole.MarkupLine("Friend Skip: {0}", followerId.ToString());
                                    continue;
                                }

                                try
                                {
                                    await _twitterClient.Blocks.CreateAsync(followerId);
                                    AnsiConsole.MarkupLine("Block durchgeführt: {0}", followerId.ToString());
                                }
                                catch (TwitterException ex)
                                {
                                    AnsiConsole.MarkupLine(ex.Message);
                                }

                                await AddBlockedId(followerId);
                                await Task.Delay(100);
                            }

                            if (iterator.RateLimit == null)
                            {
                                rateLimit = await GetFollowerRateLimit();
                                await CheckRateLimit(rateLimit, ctx);
                            }
                            else
                            {
                                await CheckRateLimit(iterator.RateLimit, ctx);
                            }

                            iterator = await _twitterClient.Followers.IdsAsync(usernameToBlock, iterator.NextCursor);

                        } while (iterator.NextCursor != 0);

                        if (!await CheckBlockedId((long)userToBlock.Id))
                        {
                            ctx.Status = $"Blocke User {userToBlock.ScreenName} | {userToBlock.Name}";
                            await _twitterClient.Blocks.CreateAsync((long)userToBlock.Id);
                            await AddBlockedId((long)userToBlock.Id);
                        }

                        AnsiConsole.MarkupLine("Benutzer {0} und Follower geblockt.", userToBlock.Name);
                    });
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

        public async Task FollowRetweets(long tweetid)
        {
            await AnsiConsole.Status()
                    .StartAsync("Lade Retweets", async ctx =>
                    {
                        ListedResponse<Status> retweets = null;

                        try
                        {
                            retweets = await _twitterClient.Statuses.RetweetsAsync(tweetid);
                        }
                        catch
                        {
                        }

                        if (retweets == null)
                        {
                            return;
                        }

                        AnsiConsole.MarkupLine("{0} Retweets gefunden", retweets.Count);

                        foreach (var status in retweets)
                        {
                            if (await CheckFollowingId((long)status.User.Id) || status.User.Id == 126127533)
                            {
                                AnsiConsole.MarkupLine("Folge bereits {0}", status.User.Id);
                                continue;
                            }

                            AnsiConsole.MarkupLine("Folge User  mit Id {0}: {1}", status.User.Id, status.User.Name);

                            try
                            {
                                var response = await _twitterClient.Friendships.CreateAsync(status.User.ScreenName);
                                await CheckRateLimit(response.RateLimit, ctx);
                                await AddFollowingId((long)status.User.Id);
                            }
                            catch
                            {
                            }
                        }
                    });
        }

        public async Task FollowFollower()
        {
            await AnsiConsole.Status()
                .StartAsync("Lade aktuelle Freunde", async ctx =>
                {
                    var friendIds = new List<long>();

                    var cursorFriends = await _twitterClient.Friends.IdsAsync();

                    do
                    {
                        foreach (var friendId in cursorFriends.Result)
                        {
                            friendIds.Add(friendId);
                        }

                        if (cursorFriends.NextCursor != 0)
                        {
                            cursorFriends = await _twitterClient.Friends.IdsAsync(cursorFriends.NextCursor);
                        }
                    } while (cursorFriends.NextCursor != 0);

                    ctx.Status = "Lade Follower";
                    var cursorFollower = await _twitterClient.Followers.IdsAsync();

                    do
                    {
                        foreach (var followerId in cursorFollower)
                        {
                            if (friendIds.Contains(followerId))
                            {
                                continue;
                            }

                            AnsiConsole.MarkupLine("Folge Id {0} zurück", followerId);
                            await _twitterClient.Friendships.CreateAsync(followerId, follow: true);
                        }

                        if (cursorFollower.NextCursor != 0)
                        {
                            cursorFollower = await _twitterClient.Followers.IdsAsync(cursorFollower.NextCursor);
                        }
                    } while (cursorFollower.NextCursor != 0);
                });
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

        public async Task<RateLimit> GetFollowerRateLimit()
        {
            var rateLimits = await _twitterClient.Application.RateLimitStatusAsync(@"followers");
            var rateLimit = rateLimits.Values.FirstOrDefault();

            return rateLimit["/followers/ids"];
        }

        public async Task<RateLimit> GetFriendsRateLimit()
        {
            var rateLimits = await _twitterClient.Application.RateLimitStatusAsync(@"friends");
            var rateLimit = rateLimits.Values.FirstOrDefault();

            return rateLimit["/friends/ids"];
        }

        public async Task<Cursor> GetCursor(string cursorName)
        {
            return await _twitContext.Cursors.Where(cursor => cursor.Name == cursorName).FirstOrDefaultAsync();
        }

        public async Task AddBlock(long id)
        {
            var blockedUser = await _twitContext.BlockedUsers.Where(blockedUser => blockedUser.ID == id).FirstOrDefaultAsync();

            if (blockedUser == null)
            {
                blockedUser = new BlockedUser();
                blockedUser.ID = id;
                await _twitContext.BlockedUsers.AddAsync(blockedUser);
            }
        }

        public async Task AddFollower(long id)
        {
            var blockedUser = await _twitContext.BlockedUsers.Where(blockedUser => blockedUser.ID == id).FirstOrDefaultAsync();

            if (blockedUser == null)
            {
                blockedUser = new BlockedUser();
                blockedUser.ID = id;
                await _twitContext.BlockedUsers.AddAsync(blockedUser);
            }
        }

        public async Task AddFriend(long id)
        {
            var blockedUser = await _twitContext.BlockedUsers.Where(blockedUser => blockedUser.ID == id).FirstOrDefaultAsync();

            if (blockedUser == null)
            {
                blockedUser = new BlockedUser();
                blockedUser.ID = id;
                await _twitContext.BlockedUsers.AddAsync(blockedUser);
            }
        }
    }
}
