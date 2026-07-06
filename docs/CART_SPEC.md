# Cart Web API, implementacijska specifikacija

- **Autor:** Slaven Robić
- **Datum:** 6.6.2026.

Specifikacija referentne implementacije Cart Web API-ja. Opisuje što se gradi: perzistencija, validacija, healthcheck, kontejnerizacija, testovi. Vizija cijele platforme je u [`ARCHITECTURE_DESIGN.md`](./ARCHITECTURE_DESIGN.md).

## 1. Opseg

U opsegu (v1):

- Web API za košaricu: kreiranje, dohvat, dodavanje/ažuriranje/uklanjanje artikala, pražnjenje.
- Perzistencija u PostgreSQL kroz EF Core 10.
- Minimalni katalog proizvoda (`products`) kao izvor za validaciju i cijenu, kroz apstrakciju `IProductCatalogReader`.
- Snapshot cijene pri dodavanju u košaricu.
- Optimistic concurrency na košarici.
- Healthcheck (liveness/readiness).
- Docker Compose za lokalno pokretanje.
- Policy-based authorization iza feature flaga (resource-server placeholder).
- Testovi: unit (domenska pravila) + integracijski.

Izvan opsega (svjesno): plaćanje/checkout/narudžbe, pun IdP i upravljanje korisnicima, full-text search, upravljanje katalogom (`products` se samo seeda).

## 2. Tehnološki stack

| Sloj | Tehnologija |
|---|---|
| Runtime | .NET 10 (LTS), C# 14 |
| Web | ASP.NET Core Minimal API |
| ORM | EF Core 10 (Npgsql) |
| Baza | PostgreSQL 17 |
| Test framework | MSTest + FluentAssertions |
| Integration host | `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) |
| Integration baza | Testcontainers for .NET (PostgreSQL) |
| Healthchecks | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| Auth (placeholder) | `Microsoft.AspNetCore.Authentication.JwtBearer` |

## 3. Struktura projekta

```
CartService.slnx
├── src/
│   ├── CartService.Api/            # Minimal API, Program.cs, DI, health, Problem Details
│   ├── CartService.Domain/         # Entiteti, domenska pravila, IProductCatalogReader
│   └── CartService.Infrastructure/ # EF DbContext, migracije, PostgresProductCatalogReader, seed
├── tests/
│   ├── CartService.UnitTests/        # domenska pravila (in-memory fake)
│   └── CartService.IntegrationTests/ # WebApplicationFactory + Testcontainers
├── docker-compose.yml
├── Dockerfile
└── docs/
    ├── ARCHITECTURE_DESIGN.md
    ├── CART_SPEC.md
    └── ADR.md
```

`Domain` ne ovisi ni o čemu, `Infrastructure` ovisi o `Domain`, `Api` ih spaja.

## 4. Model podataka

**Cart**

| Polje | Tip | Napomena |
|---|---|---|
| `Id` | `Guid` | PK |
| `OwnerId` | `Guid?` | vlasnik (`sub` iz tokena), null za guest |
| `CreatedAt` | `DateTimeOffset` | UTC |
| `UpdatedAt` | `DateTimeOffset` | UTC |
| `Items` | `List<CartItem>` | navigacijska kolekcija |
| `xmin` | (Postgres) | concurrency token (sekcija 7.2) |

**CartItem**

| Polje | Tip | Napomena |
|---|---|---|
| `Id` | `Guid` | PK |
| `CartId` | `Guid` | FK na `Cart.Id` |
| `ProductId` | `Guid` | referenca na proizvod |
| `ProductName` | `string` | snapshot imena |
| `UnitPrice` | `decimal(18,2)` | snapshot cijene |
| `Quantity` | `int` | > 0 |
| `AddedAt` | `DateTimeOffset` | UTC |

**Product** (katalog, samo za čitanje)

| Polje | Tip |
|---|---|
| `Id` | `Guid` (PK) |
| `Name` | `string` |
| `UnitPrice` | `decimal(18,2)` |
| `IsAvailable` | `bool` |

Pravila modela:

- `CartItem` je jedinstven po `(CartId, ProductId)`.
- Jedan aktivni cart po vlasniku (parcijalni unique indeks na `OwnerId` gdje nije null).
- `decimal(18,2)` za novčane iznose, sva vremena UTC.
- Brisanje `Cart`-a kaskadno briše `CartItem`-e.

## 5. API kontrakt

Bazni put `/carts`. Greške u RFC 7807 formatu (`application/problem+json`).

| # | Metoda | Ruta | Opis | Uspjeh | Greške |
|---|---|---|---|---|---|
| 1 | `POST` | `/carts` | Kreiranje prazne košarice | `201` + `CartResponse` | |
| 2 | `GET` | `/carts/{cartId}` | Dohvaćanje košarice | `200` + `CartResponse` | `404` |
| 3 | `POST` | `/carts/{cartId}/items` | Dodavanje artikla | `200` + `CartResponse` | `404`, `422` |
| 4 | `PUT` | `/carts/{cartId}/items/{productId}` | Apsolutna količina | `200` + `CartResponse` | `404`, `422` |
| 5 | `DELETE` | `/carts/{cartId}/items/{productId}` | Uklanjanje artikla | `204` | `404` |
| 6 | `DELETE` | `/carts/{cartId}/items` | Pražnjenje košarice | `204` | `404` |
| 7 | `GET` | `/health/live` | Liveness | `200` | `503` |
| 8 | `GET` | `/health/ready` | Readiness (uklj. bazu) | `200` | `503` |

Kad je `Auth:Enabled=true` (sekcija 6.5), endpointi 1 do 6 dodatno mogu vratiti `401` (nema/nevažeći token) i `403` (nedostaje scope ili tuđa košarica).

Request sheme:

```json
// POST /carts/{cartId}/items
{ "productId": "guid", "quantity": 2 }

// PUT /carts/{cartId}/items/{productId}
{ "quantity": 5 }
```

`CartResponse`:

```json
{
  "cartId": "guid",
  "items": [
    { "productId": "guid", "productName": "Primjer", "unitPrice": 19.99, "quantity": 2, "lineTotal": 39.98 }
  ],
  "totalItems": 2,
  "totalAmount": 39.98,
  "createdAt": "2026-06-30T10:00:00Z",
  "updatedAt": "2026-06-30T10:05:00Z"
}
```

`lineTotal = unitPrice * quantity` (računato). `totalItems` = zbroj količina. `totalAmount` = zbroj `lineTotal`.

## 6. Domenska pravila

### 6.1 Dodavanje artikla

1. Košarica ne postoji, `404`.
2. Proizvod se dohvaća kroz `IProductCatalogReader.GetProductAsync(productId)`.
3. Proizvod ne postoji, `422` (`product_not_found`).
4. `IsAvailable == false`, `422` (`product_unavailable`).
5. `quantity <= 0`, `422` (`invalid_quantity`).
6. Snapshot: `Name` i `UnitPrice` se preuzmu iz proizvoda i spreme u liniju.
7. Ako linija s istim `ProductId` postoji, povećava se `Quantity` (merge); cijena se ne re-snapshota.
8. Ažurira se `Cart.UpdatedAt`.

### 6.2 Ažuriranje količine

- Postavlja apsolutnu količinu (ne inkrement).
- Linija ne postoji, `404`. `quantity <= 0`, `422`. Uklanjanje ide kroz `DELETE`.

### 6.3 Snapshot cijene

Cijena u `CartItem` je snapshot iz trenutka dodavanja i ne mijenja se ako se cijena u katalogu kasnije promijeni. Re-validacija je na checkoutu (izvan opsega). Pokriveno unit testom (sekcija 9.1).

### 6.4 Validacije

| Slučaj | Status | Kod |
|---|---|---|
| Košarica ne postoji | `404` | `cart_not_found` |
| Linija ne postoji (PUT/DELETE) | `404` | `cart_item_not_found` |
| Proizvod ne postoji | `422` | `product_not_found` |
| Proizvod nedostupan | `422` | `product_unavailable` |
| Količina <= 0 | `422` | `invalid_quantity` |
| Concurrency konflikt | `409` | `concurrency_conflict` |
| Tuđa košarica (auth) | `403` | `cart_forbidden` |

### 6.5 Authorization (feature flag)

Feature flag `Auth:Enabled` (`appsettings.json`), default `false`, pa se demo pokreće bez tokena. Kad je `true`:

- JWT bearer validacija (`Authority`/`Audience` iz konfiguracije, u produkciji Keycloak).
- Scope kontrolira pristup endpointu: `cart:read` za GET (#2), `cart:write` za #1 i #3 do #6.
- Vlasništvo: pri kreiranju (#1) košarica se veže uz `sub` (`Cart.OwnerId`); bez tokena ostaje guest (`null`). Pristup košarici čiji se `OwnerId` ne poklapa sa `sub`-om vraća `403` (`cart_forbidden`).
- Statusi: `401` (nema/nevažeći token), `403` (nedostaje scope ili tuđa košarica).

Ovo je placeholder resource-server strane; izdavanje tokena i korisnici su odgovornost IdP-a.

### 6.6 Correlation ID

Middleware na svaki zahtjev čita `traceparent` (W3C Trace Context) ili `X-Correlation-ID`, a ako nedostaje generira novi `Guid`; ID se vraća u response zaglavlju. Svaka log linija nosi `correlationId`/`traceId`.

## 7. Perzistencija

### 7.1 EF Core

- `CartDbContext` s `DbSet<Cart>`, `DbSet<CartItem>`, `DbSet<Product>`.
- Konfiguracija kroz `IEntityTypeConfiguration<T>`.
- `decimal` mapiran na `numeric(18,2)`.
- Migracije generirane EF alatom, primjenjuju se na startupu.

### 7.2 Optimistic concurrency

Postgres sistemski stupac `xmin` mapiran kao concurrency token:

```csharp
builder.Property<uint>("xmin").IsRowVersion();
```

Pri konkurentnoj izmjeni iste košarice drugi `SaveChanges` baca `DbUpdateConcurrencyException`, API vraća `409` (`concurrency_conflict`).

### 7.3 Migracije i seed na startupu

1. `db.Database.Migrate()`.
2. Ako je `products` prazna, seed nekoliko proizvoda, uključujući barem jedan s `IsAvailable = false`.

## 8. Rukovanje greškama

- Sve greške vraćaju RFC 7807 `application/problem+json`.
- Globalni exception handler (`IExceptionHandler`) mapira domenske iznimke na statuse.

```json
{
  "type": "https://cartservice/errors/product_unavailable",
  "title": "Product is not available",
  "status": 422,
  "detail": "Product 'a1b2...' is not available for purchase.",
  "instance": "/carts/{cartId}/items"
}
```

## 9. Testovi

### 9.1 Unit (bez baze)

Domenska pravila kroz in-memory `IProductCatalogReader`. Pokrivaju barem: nepostojeći proizvod, nedostupan proizvod, količina <= 0, snapshot cijene, dvostruko dodavanje (merge), apsolutna količina, uklanjanje i pražnjenje, izračun `lineTotal`/`totalAmount`/`totalItems`, vlasništvo.

Imenovanje `Method_Scenario_ExpectedResult`, struktura Arrange-Act-Assert.

### 9.2 Integracijski (WebApplicationFactory + Testcontainers)

Protiv pravog Postgresa: `POST /carts` (201), pun tijek (kreiraj, dodaj, dohvati, ukloni, prazno), dodavanje u nepostojeću košaricu (404), nepostojeći/nedostupni proizvod (422), optimistic concurrency (409), perzistencija, 401 kad je auth uključen.

EF In-Memory se ne koristi jer ne reproducira transakcije, constrainte, `decimal` ni concurrency.

### 9.3 Što se ne testira

EF Core, getteri/setteri, framework kod. Ne cilja se 100% line coverage, nego pokrivenost ponašanja.

## 10. Lokalno pokretanje

`docker compose up` diže Postgres + API na `http://localhost:8080` (Swagger na `/swagger`). `docker compose down -v` briše volume. Testovi kroz `dotnet test` (integracijski traže Docker). Detalji u [`README.md`](../README.md).

## 11. Apstrakcija kataloga

```csharp
public interface IProductCatalogReader
{
    Task<ProductInfo?> GetProductAsync(Guid productId, CancellationToken ct);
}

public sealed record ProductInfo(Guid Id, string Name, decimal UnitPrice, bool IsAvailable);
```

| Implementacija | Gdje | Kad |
|---|---|---|
| `PostgresProductCatalogReader` | Infrastructure | demo/prod, SELECT iz `products` |
| `InMemoryProductCatalogReader` | tests | unit testovi |
| ES / HTTP klijent na Catalog | | produkcija |

Interface je uzak (samo get-by-id), pa je izvor podataka kataloga zamjenjiv bez izmjene logike košarice.

## 12. Kriteriji prihvaćanja

- [ ] Svih 8 endpointa radi s točnim statusima.
- [ ] Snapshot cijene se ponaša prema 6.3 (pokriveno testom).
- [ ] Optimistic concurrency vraća `409` (pokriveno testom).
- [ ] Sve greške su RFC 7807.
- [ ] `docker compose up` diže API + Postgres, healthcheck zelen.
- [ ] Migracije + seed rade na startupu.
- [ ] S `Auth:Enabled=false` demo radi bez tokena; s `true` zaštićeni endpointi vraćaju `401`/`403`.
- [ ] Unit i integracijski testovi prolaze.
- [ ] `README.md` sadrži upute za pokretanje.
- [ ] Commitovi prate razvoj inkrementalno.
