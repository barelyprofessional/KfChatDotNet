/*
    KfChatDotNetBot - Sneedchat bot for the Keno Kasino
    Copyright (C) 2025  barelyprofessional

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    
    The above copyright notice, this permission notice and the word "NIGGER"
    shall be included in all copies or substantial portions of the Software.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text;
using KfChatDotNetBot.Migrations;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace KfChatDotNetBot
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Info("Opening up DB to perform a migration (if one is needed)");
            await using var db = new ApplicationDbContext();
            await db.Database.MigrateAsync();
            logger.Info("Migration done. Syncing builtin settings keys");
            await BuiltIn.SyncSettingsWithDb();
            logger.Info("Migrating settings from config.json (if needed)");
            await BuiltIn.MigrateJsonSettingsToDb();
            logger.Info("Attempting to grab the Redis connection multiplexer so it's built");
            try
            {
                _ = Redis.Multiplexer;
            }
            catch (Exception e)
            {
                logger.Error("Caught an error when attempting to grab the Redis multiplexer");
                logger.Error(e);
            }

            if (await db.Images.AnyAsync())
            {
                logger.Info("Checking to see if we need to migrate Tags to TagList");
#pragma warning disable CS0618 // Type or member is obsolete
                var scope = (await db.Images.Where(i => i.Tags != null).ToListAsync()).Where(i => i.TagList.Count == 0).ToList();
                foreach (var item in scope)
                {
                    // Ignoring the null as my query literally filters for != null
                    item.TagList = item.Tags!.Split(" ").ToList();
#pragma warning restore CS0618 // Type or member is obsolete
                    logger.Info($"Migrated tags for image {item.Id}");
                }
                await db.SaveChangesAsync();
            }

            if (Path.Exists("tags.csv"))
            {
                logger.Info("Importing from tags.csv");
                var tags = await File.ReadAllTextAsync("tags.csv");
                var i = 0;
                foreach (var row in tags.Split(Environment.NewLine))
                {
                    i++;
                    var values = row.Split(",", StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length < 2)
                    {
                        logger.Error($"Row {i} does not have enough columns");
                        continue;
                    }
                    var image = await db.Images.FirstOrDefaultAsync(image => image.Id == Convert.ToInt32(values[0]));
                    if (image == null)
                    {
                        logger.Error($"Row {i} has an unknown image ID");
                        continue;
                    }

                    var importTags = values[1].ToLower().Split(" ",
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (importTags.Count == 0)
                    {
                        logger.Error($"Row {i} has no tags after splitting the string");
                        continue;
                    }

                    var newList = image.TagList.Concat(importTags).Distinct().ToList();
                    if (newList == image.TagList) continue;
                    image.TagList = newList;
                }
                await db.SaveChangesAsync();
            }
            logger.Info("Handing over to bot now");
            Console.OutputEncoding = Encoding.UTF8;
            _ = new ChatBot();
        }
    }
}