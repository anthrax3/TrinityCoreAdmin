﻿using System;
using System.Data;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrinityCoreAdmin.Database;

namespace TrinityCoreAdmin
{
    internal class Player
    {
        public Player()
        { }

        public Class _class
        { get; private set; }

        public uint acccountId
        { get; private set; }

        public Gender gender
        { get; private set; }

        public uint guid
        { get; private set; }

        public int money
        { get; private set; }

        public string name
        { get; set; }

        public Race race
        { get; private set; }

        public static async Task<Player> LoadFromDB(int guid)
        {
            Player p = new Player();
            await p.LoadPlayer(guid);
            return p;
        }

        private async Task LoadPlayer(int guid)
        {
            var stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_SEL_CHARACTER);
            stmt.Parameters["@guid"].Value =  guid;
            var dt = await stmt.Execute(false);

            if (dt.Rows.Count == 0)
            {
                Logger.LOG_DATABASE.Error("Player with GUID " + guid.ToString() + " not found in table `characters`, can't load.");
                return;
            }

            if (XConverter.ToInt32(dt.Rows[0][0]) != guid)
            {
                Logger.LOG_DATABASE.Error("Error during loading player with GUID " + guid.ToString() + ".");
                return;
            }

            this.guid = XConverter.ToUInt32(dt.Rows[0][0]);
            this.acccountId = XConverter.ToUInt32(dt.Rows[0][1]);
            this.name = dt.Rows[0][2].ToString();
            this.race = (Race)Enum.ToObject(typeof(Race), XConverter.ToInt32(dt.Rows[0][3]));
            this._class = (Class)Enum.ToObject(typeof(Class), dt.Rows[0][4]);
            this.gender = (Gender)Enum.ToObject(typeof(Gender), dt.Rows[0][5]);
            this.money = XConverter.ToInt32(dt.Rows[0][8]);
        }

        /// <summary>
        /// Deletes a player from the database. Flaws: Mailitems and money won't be returned.
        /// </summary>
        /// <param name="deleteFinally"></param>
        /// <returns></returns>
        public async Task DeleteFromDB(bool deleteFinally)
        {
            // TODO: Check if player is logged in. If true throw error.
            CharDeleteMethod delMethod = CharDeleteMethod.UNLINK;

            if (deleteFinally)
                delMethod = CharDeleteMethod.REMOVE;

            var stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_SEL_GUILD_LEADER_BY_GUILD_ID);
            stmt.Parameters["@guildid"].Value = await GetGuildId();

            if (await stmt.ExScalar() != null) // Player is guild leader. Change leader ingame first.
            {
                MessageBox.Show("Der Charakter " + this.name + " konnte nicht gelöscht werden, da er noch Gildenmeister ist. Bitte erst ingame degradieren.", "Fehler beim Löschen", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_GUILD_MEMBER);
            stmt.Parameters["@guid"].Value = this.guid;
            await stmt.ExNonQuery();

            await LeaveAllArenaTeams();

            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_GROUP_MEMBER);
            stmt.Parameters["@memberGuid"].Value = this.guid;
            await stmt.ExNonQuery();

            await RemovePetitionsAndSigns();

            switch (delMethod)
            {
                case CharDeleteMethod.REMOVE:
                    {
                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_SEL_CHAR_COD_ITEM_MAIL);
                        stmt.Parameters["@receiver"].Value = this.guid;
                        var dt = await stmt.Execute();

                        foreach (DataRow row in dt.Rows)
                        {
                            int mail_id = XConverter.ToInt32(row[0]);
                            int mailType = XConverter.ToInt32(row[1]);
                            int mailTemplateId = XConverter.ToInt32(row[2]);
                            int sender = XConverter.ToInt32(row[3]);
                            string subject = row[4].ToString();
                            string body = row[5].ToString();
                            int money = XConverter.ToInt32(row[6]);
                            bool has_items = Convert.ToBoolean(row[7]);

                            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_MAIL_BY_ID);
                            stmt.Parameters["@id"].Value = mail_id;
                            await stmt.ExNonQuery();

                            // Mail is not from player
                            if (mailType != 0) // MAIL_NORMAL
                            {
                                if (has_items)
                                {
                                    stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_MAIL_ITEM_BY_ID);
                                    stmt.Parameters["@mail_id"].Value = mail_id;
                                    await stmt.ExNonQuery();
                                }
                                continue;
                            }

                            if (has_items)
                            {
                                stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_SEL_MAILITEMS);
                                stmt.Parameters["@mail_id"].Value = mail_id;
                                var dtItems = await stmt.Execute();

                                foreach (DataRow rowItem in dt.Rows)
                                {
                                    int item_guidlow = XConverter.ToInt32(rowItem[11]);

                                    stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_ITEM_INSTANCE);
                                    stmt.Parameters["@guid"].Value = item_guidlow;
                                    await stmt.ExNonQuery();

                                    continue;
                                }
                            }

                            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_MAIL_ITEM_BY_ID);
                            stmt.Parameters["@mail_id"].Value = mail_id;
                            await stmt.ExNonQuery();
                        }

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_SEL_CHAR_PETS);
                        stmt.Parameters["@owner"].Value = this.guid;
                        dt = await stmt.Execute();

                        foreach (DataRow row in dt.Rows)
                        {
                            int petguidlow = XConverter.ToInt32(row[0]);
                            await DeletePetFromDB(petguidlow);
                        }

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHARACTER);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_PLAYER_ACCOUNT_DATA);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_DECLINED_NAME);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_ACTION);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_AURA);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_GIFT);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_PLAYER_HOMEBIND);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_INSTANCE);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_INVENTORY);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_QUESTSTATUS);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_QUESTSTATUS_REWARDED);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_REPUTATION);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_SPELL);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_SPELL_COOLDOWN);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_PLAYER_GM_TICKETS);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_ITEM_INSTANCE_BY_OWNER);
                        stmt.Parameters["@owner_guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_SOCIAL_BY_FRIEND);
                        stmt.Parameters["@friend"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_SOCIAL_BY_GUID);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_MAIL);
                        stmt.Parameters["@receiver"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_MAIL_ITEMS);
                        stmt.Parameters["@receiver"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_PET_BY_OWNER);
                        stmt.Parameters["@owner"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_PET_DECLINEDNAME_BY_OWNER);
                        stmt.Parameters["@owner"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_ACHIEVEMENTS);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_ACHIEVEMENT_PROGRESS);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_EQUIPMENTSETS);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_GUILD_EVENTLOG_BY_PLAYER);
                        stmt.Parameters["@PlayerGuid1"].Value = this.guid;
                        stmt.Parameters["@PlayerGuid2"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_GUILD_BANK_EVENTLOG_BY_PLAYER);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_PLAYER_BGDATA);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_GLYPHS);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_QUESTSTATUS_DAILY);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_TALENT);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_SKILLS);
                        stmt.Parameters["@guid"].Value = this.guid;
                        await stmt.ExNonQuery();

                        break;
                    }
                case CharDeleteMethod.UNLINK:
                    stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_UPD_DELETE_INFO);
                    stmt.Parameters["@guid"].Value = this.guid;
                    await stmt.ExNonQuery();
                    break;
            }
        }

        public async Task<int> GetGuildId()
        {
            var stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_SEL_GUILDID_BY_GUID);
            stmt.Parameters["@guid"].Value = this.guid;
            var result = await stmt.ExScalar();

            if (result == null)
                return 0;

            return (int)result;
        }

        private async Task LeaveAllArenaTeams()
        {
            var stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_SEL_PLAYER_ARENA_TEAMS);
            stmt.Parameters["@guid"].Value = this.guid;
            var dt = await stmt.Execute();

            foreach (DataRow row in dt.Rows)
            {
                int arenaTeamId = (int)row[0];
                if (arenaTeamId != 0)
                {
                    stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_ARENA_TEAM_MEMBER);
                    stmt.Parameters["@arenaTeamId"].Value = arenaTeamId;
                    stmt.Parameters["@guid"].Value = this.guid;
                    await stmt.ExNonQuery();
                }
            }
        }

        private async Task RemovePetitionsAndSigns()
        {
            var stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_ALL_PETITION_SIGNATURES);
            stmt.Parameters["@playerguid"].Value = this.guid;
            await stmt.ExNonQuery();

            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_PETITION_BY_OWNER);
            stmt.Parameters["@ownerguid"].Value = this.guid;
            await stmt.ExNonQuery();

            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_PETITION_SIGNATURE_BY_OWNER);
            stmt.Parameters["@ownerguid"].Value = this.guid;
            await stmt.ExNonQuery();
        }

        private async Task DeletePetFromDB(int guid)
        {
            var stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_PET_BY_ID);
            stmt.Parameters["@id"].Value = this.guid;
            await stmt.ExNonQuery();

            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_CHAR_PET_DECLINEDNAME);
            stmt.Parameters["@id"].Value = this.guid;
            await stmt.ExNonQuery();

            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_PET_AURAS);
            stmt.Parameters["@guid"].Value = this.guid;
            await stmt.ExNonQuery();

            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_PET_SPELLS);
            stmt.Parameters["@guid"].Value = this.guid;
            await stmt.ExNonQuery();

            stmt = ServerManager.charDB.GetPreparedStatement(CharDatabase.CharDatabaseStatements.CHAR_DEL_PET_SPELL_COOLDOWNS);
            stmt.Parameters["@guid"].Value = this.guid;
            await stmt.ExNonQuery();
        }
    }
}