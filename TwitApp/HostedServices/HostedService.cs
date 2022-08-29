using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwitApp.Services;

namespace TwitApp.HostedServices
{
    public class HostedService : BackgroundService
    {
        private readonly IHostApplicationLifetime _hostLifetime;
        private readonly IHostEnvironment _hostingEnv;
        private readonly IConfiguration _configuration;

        private readonly ITwitService _twitService;

        public HostedService(
            IHostApplicationLifetime hostLifetime,
            IHostEnvironment hostingEnv,
            IConfiguration configuration,
            ITwitService twitService)
        {
            _hostLifetime = hostLifetime;
            _hostingEnv = hostingEnv;
            _configuration = configuration;
            _twitService = twitService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _twitService.ApplyMigrations();

            var menuLoadBlocked = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.LoadBlocked);
            var menuLoadFollower = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.LoadFollower);
            var menuLoadFriends = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.LoadFriends);
            var menuBlockUser = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.BlockUser);
            var menuBlockUserInput = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.BlockUserInput);
            var menuUnblockUser = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.UnblockUser);
            var menuBlockRecursive = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.BlockRecursive);
            var menuFollowStatusRetweets = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.FollowStatusRetweets);
            var menuLoadUsername = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.LoadUsername);
            var menuShowDbCount = new MenuItems.MainMenu(MenuItems.MainMenu.MenuType.ShowDatabaseCounts);

            var menuSelection = AnsiConsole.Prompt(new SelectionPrompt<MenuItems.MainMenu>().Title("[yellow]Hauptmenu[/]")
                                                                                            .PageSize(10)
                                                                                            .AddChoices(new[]
                                                                                            {
                                                                                                menuLoadBlocked,
                                                                                                menuLoadFollower,
                                                                                                menuLoadFriends,
                                                                                                menuBlockUser,
                                                                                                menuBlockUserInput,
                                                                                                menuUnblockUser,
                                                                                                menuBlockRecursive,
                                                                                                menuFollowStatusRetweets,
                                                                                                menuLoadUsername,
                                                                                                menuShowDbCount
                                                                                            }));

            switch (menuSelection.Type)
            {
                case MenuItems.MainMenu.MenuType.LoadBlocked:
                    await _twitService.LoadBlockedUsers();
                    break;

                case MenuItems.MainMenu.MenuType.LoadFollower:
                    await _twitService.LoadFollower();
                    break;

                case MenuItems.MainMenu.MenuType.LoadFriends:
                    await _twitService.LoadFriends();
                    break;

                case MenuItems.MainMenu.MenuType.BlockUser:
                    await _twitService.BlockUserAndFollower();
                    break;

                case MenuItems.MainMenu.MenuType.BlockUserInput:
                    var usernameToBlock = AnsiConsole.Ask<string>("[green]Benutzernamen[/] eingeben:");
                    await _twitService.BlockUsername(usernameToBlock);
                    break;

                case MenuItems.MainMenu.MenuType.UnblockUser:
                    var usernameToUnblock = AnsiConsole.Ask<string>("[green]Benutzernamen[/] eingeben:");
                    await _twitService.UnblockUserAndFollower(usernameToUnblock);
                    break;

                case MenuItems.MainMenu.MenuType.BlockRecursive:
                    var usernameToBlockRecursive = AnsiConsole.Ask<string>("[green]Benutzernamen[/] eingeben:");
                    var depth = AnsiConsole.Ask<int>("[green]Rekursions Tiefe[/] eingeben:");
                    await _twitService.BlockRecursive(usernameToBlockRecursive, depth);
                    break;

                case MenuItems.MainMenu.MenuType.FollowStatusRetweets:
                    var statusIdString = AnsiConsole.Ask<string>("[green]Status ID[/] eingeben:");
                    var statusId = Convert.ToInt64(statusIdString);
                    await _twitService.FollowRetweets(statusId);
                    break;

                case MenuItems.MainMenu.MenuType.LoadUsername:
                    var idString = AnsiConsole.Ask<string>("[green]ID[/] eingeben:");
                    long id = Convert.ToInt64(idString);
                    var username = await _twitService.GetUsername(id);
                    AnsiConsole.MarkupLine($"[red]{username}[/]");
                    break;

                case MenuItems.MainMenu.MenuType.ShowDatabaseCounts:
                    var countBlocked = await _twitService.GetDbBlockedCount();
                    var countFollower = await _twitService.GetDbFollowerCount();
                    var countFriends = await _twitService.GetDbFriendCount();

                    AnsiConsole.MarkupLine($"[red]Blocked:[/] { countBlocked}");
                    AnsiConsole.MarkupLine($"[red]Follower:[/] {countFollower}");
                    AnsiConsole.MarkupLine($"[red]Friends:[/] {countFriends}");

                    break;
            }

            Console.ReadKey();
            _hostLifetime.StopApplication();
        }
    }
}
