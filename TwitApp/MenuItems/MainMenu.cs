using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TwitApp.MenuItems
{
    public class MainMenu
    {
        public enum MenuType
        {
            LoadBlocked,
            LoadFollower,
            LoadFriends,
            BlockUser,
            BlockUserInput,
            UnblockUser,
            BlockRecursive,
            FollowStatusRetweets,
            FollowFollower,
            LoadUsername,
            ShowDatabaseCounts
        }

        public MainMenu(MenuType menuType)
        {
            Type = menuType;
        }

        public MenuType Type { get; set; }

        public override string ToString()
        {
            switch (Type)
            {
                case MenuType.LoadBlocked:
                    return "Lade geblockte User";

                case MenuType.LoadFollower:
                    return "Lade Follower";

                case MenuType.LoadFriends:
                    return "Lade Freunde";

                case MenuType.BlockUser:
                    return "Block User und Follower";

                case MenuType.BlockUserInput:
                    return "Block einzelnen User und Follower";

                case MenuType.UnblockUser:
                    return "Unblock User und Follower";

                case MenuType.BlockRecursive:
                    return "Block Rekursiv";

                case MenuType.FollowStatusRetweets:
                    return "Folge Retweet Benutzern eines Status";

                case MenuType.LoadUsername:
                    return "Lade Benutzernamen anhand von ID";

                case MenuType.ShowDatabaseCounts:
                    return "Zeige Anzahl Datenbank Einträge";

                case MenuType.FollowFollower:
                    return "Folge Followern";

                default:
                    return "WTF?";
            }
        }
    }
}
