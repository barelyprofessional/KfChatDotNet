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
            logger.Info("Handing over to bot now");
            Console.OutputEncoding = Encoding.UTF8;
            new ChatBot();
        }
    }
}