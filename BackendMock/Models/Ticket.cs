namespace BackendMock.Models
{
    public class Ticket
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}