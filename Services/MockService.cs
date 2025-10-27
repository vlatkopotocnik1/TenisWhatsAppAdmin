using WhatsAppAdmin.Models;


namespace WhatsAppAdmin.Services
{
    public class MockService : IService
    {
        private readonly List<WhatsAppGroup> _whatsAppGroups;


        public MockService()
        {
            // seed mock data
            _whatsAppGroups = new List<WhatsAppGroup>
{
new WhatsAppGroup { Id = "1", Name = "WhatsAppGroup 1", Users = new List<Users>
{
new Users { Name = "Marko Novak", Rating = 1600, PhoneNumber = "0123456789" },
new Users { Name = "Ana Kostic", Rating = 1580, PhoneNumber = "0123456789"  },
new Users { Name = "Ivan Horvat", Rating = 1560, PhoneNumber = "0123456789"  },
new Users { Name = "Petra Ristic", Rating = 1540, PhoneNumber = "0123456789"  },
}},


new WhatsAppGroup { Id = "2a", Name = "WhatsAppGroup 2A", Users = new List<Users>
{
new Users { Name = "Luka Peric", Rating = 1500, PhoneNumber = "0123456789"  },
new Users { Name = "Maja Kovac", Rating = 1490, PhoneNumber = "0123456789"  },
new Users { Name = "Tina Jovanovic", Rating = 1480, PhoneNumber = "0123456789"  },
new Users { Name = "Goran Sopic", Rating = 1470, PhoneNumber = "0123456789"  },
}},


new WhatsAppGroup { Id = "2b", Name = "WhatsAppGroup 2B", Users = new List<Users>
{
new Users { Name = "Dario Marin", Rating = 1460, PhoneNumber = "0123456789"  },
new Users { Name = "Katarina Zec", Rating = 1450, PhoneNumber = "0123456789"  },
new Users { Name = "Neven Kralj", Rating = 1440, PhoneNumber = "0123456789"  },
new Users { Name = "Sara Filipovic", Rating = 1430, PhoneNumber = "0123456789"  },
}},


new WhatsAppGroup { Id = "3a", Name = "WhatsAppGroup 3A", Users = new List<Users>
{
new Users { Name = "Matej Suster", Rating = 1400, PhoneNumber = "0123456789"  },
new Users { Name = "Ivana Perkovic", Rating = 1390, PhoneNumber = "0123456789"  },
new Users { Name = "Bruno Zoric", Rating = 1380, PhoneNumber = "0123456789"  },
new Users { Name = "Lea Novak", Rating = 1370, PhoneNumber = "0123456789"  },
}},


new WhatsAppGroup { Id = "3b", Name = "WhatsAppGroup 3B", Users = new List<Users>
{
new Users { Name = "Domagoj Vuk", Rating = 1360, PhoneNumber = "0123456789"  },
new Users { Name = "Nina Maric", Rating = 1350, PhoneNumber = "0123456789"  },
new Users { Name = "Kristijan Leko", Rating = 1340, PhoneNumber = "0123456789"  },
new Users { Name = "Marija Balen", Rating = 1330, PhoneNumber = "0123456789"  },
}},


new WhatsAppGroup { Id = "3c", Name = "WhatsAppGroup 3C", Users = new List<Users>
{
new Users { Name = "Filip Grubic", Rating = 1320, PhoneNumber = "0123456789"  },
new Users { Name = "Ivona Basic", Rating = 1310, PhoneNumber = "0123456789"  },
new Users { Name = "Zoran Dedic", Rating = 1300, PhoneNumber = "0123456789"  },
new Users { Name = "Ema Novak", Rating = 1290, PhoneNumber = "0123456789"  },
}},
};
        }
        public Task<List<WhatsAppGroup>> GetWhatsAppGroupsAsync()
        {
            // return deep copy to simulate remote call
            var result = _whatsAppGroups.Select(l => new WhatsAppGroup
            {
                Id = l.Id,
                Name = l.Name,
                Users = l.Users.Select(p => new Users { Id = p.Id, Name = p.Name, Rating = p.Rating, Notes = p.Notes, PhoneNumber = p.PhoneNumber }).ToList()
            }).ToList();


            return Task.FromResult(result);
        }


        public Task<List<WhatsAppGroup>> MoveUserAsync(Guid userId, string fromWhatsAppGroupId, string toWhatsAppGroupId)
        {
            var from = _whatsAppGroups.FirstOrDefault(x => x.Id == fromWhatsAppGroupId);
            var to = _whatsAppGroups.FirstOrDefault(x => x.Id == toWhatsAppGroupId);
            if (from == null || to == null) return Task.FromResult(_whatsAppGroups);


            var user = from.Users.FirstOrDefault(p => p.Id == userId);
            if (user == null) return Task.FromResult(_whatsAppGroups);


            // remove and add to target
            from.Users.Remove(user);
            to.Users.Add(user);


            // simulate remote save latency
            return Task.Delay(150).ContinueWith(_ => _whatsAppGroups.Select(l => new WhatsAppGroup
            {
                Id = l.Id,
                Name = l.Name,
                Users = l.Users.Select(p => new Users { Id = p.Id, Name = p.Name, Rating = p.Rating, Notes = p.Notes, PhoneNumber = p.PhoneNumber }).ToList()
            }).ToList());
        }


        public Task<List<WhatsAppGroup>> SaveWhatsAppGroupsAsync(List<WhatsAppGroup> whatsAppGroups)
        {
            // Overwrite mock and return copy
            _whatsAppGroups.Clear();
            foreach (var l in whatsAppGroups)
            {
                _whatsAppGroups.Add(new WhatsAppGroup
                {
                    Id = l.Id,
                    Name = l.Name,
                    Users = l.Users.Select(p => new Users { Id = p.Id, Name = p.Name, Rating = p.Rating, Notes = p.Notes, PhoneNumber = p.PhoneNumber }).ToList()
                });
            }


            return Task.FromResult(_whatsAppGroups.Select(l => new WhatsAppGroup
            {
                Id = l.Id,
                Name = l.Name,
                Users = l.Users.Select(p => new Users { Id = p.Id, Name = p.Name, Rating = p.Rating, Notes = p.Notes, PhoneNumber = p.PhoneNumber }).ToList()
            }).ToList());
        }
    }
}