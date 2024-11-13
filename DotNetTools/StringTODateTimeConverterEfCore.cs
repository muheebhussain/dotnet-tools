using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

public class NullableStringToDateTimeConverter : ValueConverter<DateTime?, string>
{
    public NullableStringToDateTimeConverter() 
        : base(
            date => date.HasValue ? date.Value.ToString("yyyy-MM-dd") : null,
            str => ParseDate(str))
    {
    }

    private static DateTime? ParseDate(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return null;
        }
        
        return DateTime.TryParseExact(str, "yyyy-MM-dd", 
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) 
            ? date 
            : (DateTime?)null;
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
