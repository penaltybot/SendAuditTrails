using MySql.Data.MySqlClient;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;

namespace SendAuditTrails
{
    public class Program
    {
        public static void Main()
        {
            Directory.CreateDirectory(@"/logs");

            StringBuilder logOutput = new StringBuilder();

            try
            {
                logOutput.AppendLine(String.Format("[{0}]       [-] Audit Trail script starting", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [-] Retrieving necessary environment variables", DateTime.Now.ToString()));
                string connectionString = string.Format(
                    "server={0};user={1};password={2};port={3};database={4}",
                    new string[]
                    {
                    Environment.GetEnvironmentVariable("DB_URL"),
                    Environment.GetEnvironmentVariable("DB_USER"),
                    Environment.GetEnvironmentVariable("DB_PASSWORD"),
                    Environment.GetEnvironmentVariable("DB_PORT"),
                    Environment.GetEnvironmentVariable("DB_DATABASE")
                    });

                logOutput.AppendLine(String.Format("[{0}]       [-] Opening connection to MySQL", DateTime.Now.ToString()));
                MySqlConnection connection = new MySqlConnection(connectionString);
                connection.Open();
                logOutput.AppendLine(String.Format("[{0}]       [-] Connection to MySQL successfully opened", DateTime.Now.ToString()));

                List<QueriedMatch.QueriedMatch> startedMatches = GetStartedMatches(connection);

                if (IsEmpty(startedMatches))
                {
                    logOutput.AppendLine(String.Format("[{0}]       [-] No matches started", DateTime.Now.ToString()));
                    logOutput.AppendLine(String.Format("[{0}]       [-] Job finished", DateTime.Now.ToString()));
                    ProcessLogs(logOutput.ToString());

                    return;
                }

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting global constants", DateTime.Now.ToString()));
                Dictionary<string, string> globalConstants = GetGlobalConstants(connection);

                var apiUrlTelegram = globalConstants["API_URL_TELEGRAM"];
                var telegramBotToken = globalConstants["TOKEN_BOT_TELEGRAM"];
                var telegramChatId = globalConstants["CHAT_ID_TELEGRAM"];
                var reportUrl = globalConstants["WEBSITE_LOCATION"];

                foreach (var startedMatch in startedMatches)
                {
                    startedMatch.Md5Hash = GetMd5(connection, startedMatch.IdMatchApi);

                    logOutput.AppendLine(String.Format(
                        "[{0}]       [+] Logging MD5 Hash for FixtureId='{1}'",
                        DateTime.Now.ToString(),
                        startedMatch.IdMatchApi));
                    SendTelegramMessage(apiUrlTelegram, telegramBotToken, telegramChatId, startedMatch, reportUrl);

                    logOutput.AppendLine(String.Format(
                        "[{0}]       [+] Toggling 'AuditTrailSent' for FixtureId='{1}'",
                        DateTime.Now.ToString(),
                        startedMatch.IdMatchApi));
                    ToggleAuditTrailSent(connection, startedMatch);
                }

                logOutput.AppendLine(String.Format("[{0}]       [-] Closing connection to MySQL", DateTime.Now.ToString()));
                connection.Close();
                logOutput.AppendLine(String.Format("[{0}]       [-] Connection to MySQL successful closed", DateTime.Now.ToString()));
                logOutput.AppendLine(String.Format("[{0}]       [-] Audit Trail script finished", DateTime.Now.ToString()));
            }
            catch (Exception ex)
            {
                logOutput.AppendLine(String.Format("[{0}]       [!] Audit Trail script failed with exception:", DateTime.Now.ToString()));
                logOutput.AppendLine(ex.ToString());
            }

            ProcessLogs(logOutput.ToString());
        }

        private static void ToggleAuditTrailSent(MySqlConnection connection, QueriedMatch.QueriedMatch startedMatch)
        {
            MySqlCommand command = new MySqlCommand("UpdateStartedMatch", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            command.Parameters.Add(new MySqlParameter("IdmatchAPI", startedMatch.IdMatchApi));

            command.ExecuteNonQuery();
        }

        private static void SendTelegramMessage(string apiUrlTelegram, string telegramBotToken, string telegramChatId, QueriedMatch.QueriedMatch startedMatch, string reportUrl)
        {
            var apiTelegramUrl = new RestClient(apiUrlTelegram + telegramBotToken + "/sendMessage");

            var apiTelegramRequest = new RestRequest();
            apiTelegramRequest.AddQueryParameter("chat_id", telegramChatId);
            apiTelegramRequest.AddQueryParameter("text", GetText(startedMatch, reportUrl));
            apiTelegramRequest.AddQueryParameter("parse_mode", "HTML");

            Thread.Sleep(5000);
            apiTelegramUrl.Execute(apiTelegramRequest);
        }

        private static string GetMd5(MySqlConnection connection, int idMatchApi)
        {
            MySqlCommand getMd5MatchBetsCommand = new MySqlCommand("GetMd5MatchBets", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            getMd5MatchBetsCommand.Parameters.Add(new MySqlParameter("IdmatchAPI", idMatchApi));

            MySqlDataReader getMd5MatchBetsReader = getMd5MatchBetsCommand.ExecuteReader();

            string md5 = null;
            if (getMd5MatchBetsReader.HasRows)
            {
                getMd5MatchBetsReader.Read();

                md5 = Convert.ToString(getMd5MatchBetsReader["MD5"]);
            }

            getMd5MatchBetsReader.Close();

            return md5;
        }

        private static string GetText(QueriedMatch.QueriedMatch startedMatch, string reportUrl)
        {
            return "Jornada " + startedMatch.Matchday + ": <b>" +
                startedMatch.HomeTeam + "</b> - <b>" + startedMatch.AwayTeam + "</b>\nMD5 Hash: <code>" +
                startedMatch.Md5Hash + "</code>\nReport: " + reportUrl + "/Home/Audit?IdmatchAPI=" + startedMatch.IdMatchApi;
        }

        private static bool IsEmpty(List<QueriedMatch.QueriedMatch> list)
        {
            return !(list.Count > 0);
        }

        private static void ProcessLogs(string logOutput)
        {
            string logOutputPath = "/logs/log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";

            File.AppendAllText(logOutputPath, logOutput);

            if (logOutput.Contains("[!]"))
            {
                var fromAddress = Environment.GetEnvironmentVariable("FROM_EMAIL");
                string operatorEmails = Environment.GetEnvironmentVariable("OPERATOR_EMAILS");
                string emailPassword = Environment.GetEnvironmentVariable("EMAIL_PASSWORD");

                var smtp = new SmtpClient
                {
                    Host = "smtp.gmail.com",
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromAddress, emailPassword)
                };

                using var message = new MailMessage(fromAddress, operatorEmails)
                {
                    Subject = "Errors in job!",
                    Body = "Hi operators,<br><br>An error has occured in today's scheduled job!<br>Log file has been attached to this message.",
                    IsBodyHtml = true,
                };

                message.Attachments.Add(new Attachment(logOutputPath));

                smtp.Send(message);
            }

            string[] files = Directory.GetFiles("/logs");

            foreach (string file in files)
            {
                FileInfo fileInfo = new FileInfo(file);
                if (fileInfo.LastAccessTime < DateTime.Now.AddHours(-1))
                {
                    fileInfo.Delete();
                }
            }
        }

        private static List<QueriedMatch.QueriedMatch> GetStartedMatches(MySqlConnection connection)
        {
            MySqlCommand startedMatchesCommand = new MySqlCommand("GetStartedMatches", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader startedMatchesReader = startedMatchesCommand.ExecuteReader();

            List<QueriedMatch.QueriedMatch> queriedMatch = new List<QueriedMatch.QueriedMatch>();
            while (startedMatchesReader.Read())
            {
                queriedMatch.Add(new QueriedMatch.QueriedMatch()
                {
                    IdMatchApi = startedMatchesReader.GetInt32("IdmatchAPI"),
                    Matchday = startedMatchesReader.GetInt32("Matchday"),
                    HomeTeam = startedMatchesReader.GetString("HomeTeam"),
                    AwayTeam = startedMatchesReader.GetString("AwayTeam"),
                    Md5Hash = null
                });
            }

            startedMatchesReader.Close();

            return queriedMatch;
        }

        private static Dictionary<string, string> GetGlobalConstants(MySqlConnection connection)
        {
            MySqlCommand globalConstantsCommand = new MySqlCommand("GetGlobalConstants", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader globalConstantsReader = globalConstantsCommand.ExecuteReader();

            Dictionary<string, string> globalConstants = new Dictionary<string, string>();
            while (globalConstantsReader.Read())
            {
                globalConstants.Add(
                    globalConstantsReader["Constant"].ToString(),
                    globalConstantsReader["Value"].ToString());
            }

            globalConstantsReader.Close();

            return globalConstants;
        }
    }
}