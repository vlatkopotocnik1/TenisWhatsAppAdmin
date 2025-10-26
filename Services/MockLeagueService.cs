using TenisWhatsAppAdmin.Models;


namespace TenisWhatsAppAdmin.Services
{
    public class MockLeagueService : ILeagueService
    {
        private readonly List<League> _leagues;


        public MockLeagueService()
        {
            // seed mock data
            _leagues = new List<League>
{
new League { Id = "1", Name = "League 1", Players = new List<Player>
{
new Player { Name = "Marko Novak", Rating = 1600 },
new Player { Name = "Ana Kostic", Rating = 1580 },
new Player { Name = "Ivan Horvat", Rating = 1560 },
new Player { Name = "Petra Ristic", Rating = 1540 },
}},


new League { Id = "2a", Name = "League 2A", Players = new List<Player>
{
new Player { Name = "Luka Peric", Rating = 1500 },
new Player { Name = "Maja Kovac", Rating = 1490 },
new Player { Name = "Tina Jovanovic", Rating = 1480 },
new Player { Name = "Goran Sopic", Rating = 1470 },
}},


new League { Id = "2b", Name = "League 2B", Players = new List<Player>
{
new Player { Name = "Dario Marin", Rating = 1460 },
new Player { Name = "Katarina Zec", Rating = 1450 },
new Player { Name = "Neven Kralj", Rating = 1440 },
new Player { Name = "Sara Filipovic", Rating = 1430 },
}},


new League { Id = "3a", Name = "League 3A", Players = new List<Player>
{
new Player { Name = "Matej Suster", Rating = 1400 },
new Player { Name = "Ivana Perkovic", Rating = 1390 },
new Player { Name = "Bruno Zoric", Rating = 1380 },
new Player { Name = "Lea Novak", Rating = 1370 },
}},


new League { Id = "3b", Name = "League 3B", Players = new List<Player>
{
new Player { Name = "Domagoj Vuk", Rating = 1360 },
new Player { Name = "Nina Maric", Rating = 1350 },
new Player { Name = "Kristijan Leko", Rating = 1340 },
new Player { Name = "Marija Balen", Rating = 1330 },
}},


new League { Id = "3c", Name = "League 3C", Players = new List<Player>
{
new Player { Name = "Filip Grubic", Rating = 1320 },
new Player { Name = "Ivona Basic", Rating = 1310 },
new Player { Name = "Zoran Dedic", Rating = 1300 },
new Player { Name = "Ema Novak", Rating = 1290 },
}},
};
        }
        public Task<List<League>> GetLeaguesAsync()
        {
            // return deep copy to simulate remote call
            var result = _leagues.Select(l => new League
            {
                Id = l.Id,
                Name = l.Name,
                Players = l.Players.Select(p => new Player { Id = p.Id, Name = p.Name, Rating = p.Rating, Notes = p.Notes }).ToList()
            }).ToList();


            return Task.FromResult(result);
        }


        public Task<List<League>> MovePlayerAsync(Guid playerId, string fromLeagueId, string toLeagueId)
        {
            var from = _leagues.FirstOrDefault(x => x.Id == fromLeagueId);
            var to = _leagues.FirstOrDefault(x => x.Id == toLeagueId);
            if (from == null || to == null) return Task.FromResult(_leagues);


            var player = from.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) return Task.FromResult(_leagues);


            // remove and add to target
            from.Players.Remove(player);
            to.Players.Add(player);


            // simulate remote save latency
            return Task.Delay(150).ContinueWith(_ => _leagues.Select(l => new League
            {
                Id = l.Id,
                Name = l.Name,
                Players = l.Players.Select(p => new Player { Id = p.Id, Name = p.Name, Rating = p.Rating, Notes = p.Notes }).ToList()
            }).ToList());
        }


        public Task<List<League>> SaveLeaguesAsync(List<League> leagues)
        {
            // Overwrite mock and return copy
            _leagues.Clear();
            foreach (var l in leagues)
            {
                _leagues.Add(new League
                {
                    Id = l.Id,
                    Name = l.Name,
                    Players = l.Players.Select(p => new Player { Id = p.Id, Name = p.Name, Rating = p.Rating, Notes = p.Notes }).ToList()
                });
            }


            return Task.FromResult(_leagues.Select(l => new League
            {
                Id = l.Id,
                Name = l.Name,
                Players = l.Players.Select(p => new Player { Id = p.Id, Name = p.Name, Rating = p.Rating, Notes = p.Notes }).ToList()
            }).ToList());
        }
    }
}