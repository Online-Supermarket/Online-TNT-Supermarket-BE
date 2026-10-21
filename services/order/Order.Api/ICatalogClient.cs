namespace Order.Api;

public interface ICatalogClient
{
    Task<ProductDto?> Product(Guid id);
    Task<ReservationResponse?> Reserve(Guid orderId, List<ReservationLine> lines, Guid correlation);
    Task<ReservationResponse?> Release(Guid orderId, Guid correlation);
    Task<ReservationResponse?> Reservation(Guid orderId);
}
