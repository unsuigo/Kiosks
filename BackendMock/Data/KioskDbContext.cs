using BackendMock.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendMock.Data
{
    public class KioskDbContext : DbContext
    {
        public KioskDbContext(
            DbContextOptions<KioskDbContext> options)
            : base(options)
        {
        }

        public DbSet<Ticket> Tickets => Set<Ticket>();
    }
}