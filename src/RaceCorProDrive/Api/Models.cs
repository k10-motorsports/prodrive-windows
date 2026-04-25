using System.Text.Json.Serialization;

namespace RaceCorProDrive.Api
{
    public class SessionsResponse
    {
        [JsonPropertyName("sessions")]
        public List<Session> Sessions { get; set; } = new();

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("hasMore")]
        public bool HasMore { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }
    }

    public class Session
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }

        [JsonPropertyName("carName")]
        public string? CarName { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("subsessionId")]
        public string? SubsessionId { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }

    public class SessionDetailResponse
    {
        [JsonPropertyName("session")]
        public Session? Session { get; set; }

        [JsonPropertyName("laps")]
        public List<Lap>? Laps { get; set; }

        [JsonPropertyName("behavior")]
        public object? Behavior { get; set; }
    }

    public class Lap
    {
        [JsonPropertyName("lapNumber")]
        public int LapNumber { get; set; }

        [JsonPropertyName("lapTime")]
        public TimeSpan LapTime { get; set; }

        [JsonPropertyName("delta")]
        public TimeSpan? Delta { get; set; }

        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
    }

    public class DashboardResponse
    {
        [JsonPropertyName("stats")]
        public DashboardStats? Stats { get; set; }

        [JsonPropertyName("recent")]
        public List<Session>? Recent { get; set; }

        [JsonPropertyName("_stub")]
        public bool IsStub { get; set; }

        [JsonPropertyName("_todo")]
        public List<string>? Todo { get; set; }
    }

    public class DashboardStats
    {
        [JsonPropertyName("totalRaces")]
        public int TotalRaces { get; set; }

        [JsonPropertyName("uniqueTracks")]
        public int UniqueTracks { get; set; }

        [JsonPropertyName("uniqueCars")]
        public int UniqueCars { get; set; }

        [JsonPropertyName("careerDays")]
        public int CareerDays { get; set; }
    }
}
