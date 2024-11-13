using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

public class NullableStringToDateTimeConverter : ValueConverter<DateTime?, string>
{
    public NullableStringToDateTimeConverter() 
        : base(
            date => date.HasValue ? date.Value.ToString("yyyy-MM-dd") : null,  // Convert DateTime? to string
            str => DateTime.TryParse(str, out var date) ? (DateTime?)date : null) // Convert string to DateTime?
    {
    }
}
using Microsoft.EntityFrameworkCore;
using System;

public class YourDbContext : DbContext
{
    public DbSet<YourEntity> YourEntities { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dateConverter = new NullableStringToDateTimeConverter();

        modelBuilder.Entity<YourEntity>()
            .Property(e => e.StartDate)
            .HasConversion(dateConverter);

        modelBuilder.Entity<YourEntity>()
            .Property(e => e.MaturityDate)
            .HasConversion(dateConverter);
    }
}
