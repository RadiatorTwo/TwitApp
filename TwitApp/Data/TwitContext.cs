using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwitApp.Models;

namespace TwitApp.Data
{
    public class TwitContext : DbContext
    {
        public DbSet<BlockedUser> BlockedUsers { get; set; }
        public DbSet<Follower> Follower { get; set; }
        public DbSet<Friend> Friends { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "Twit.db");
            
            if (!Directory.Exists(Path.GetDirectoryName(dbPath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
            }

            optionsBuilder.UseSqlite("Filename = " + dbPath);
            optionsBuilder.EnableSensitiveDataLogging();

            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BlockedUser>().ToTable("BlockedUsers");
            modelBuilder.Entity<Follower>().ToTable("Follower");
            modelBuilder.Entity<Friend>().ToTable("Friends");
        }
    }
}
