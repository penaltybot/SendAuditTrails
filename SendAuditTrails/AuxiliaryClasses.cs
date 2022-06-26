namespace QueriedMatch
{
    public class QueriedMatch
    {
        public QueriedMatch()
        {
        }

        public int IdMatchApi { get; set; }

        public int? Matchday { get; set; }

        public string HomeTeam { get; set; }

        public string AwayTeam { get; set; }

        public string Md5Hash { get; set; }
    }
}