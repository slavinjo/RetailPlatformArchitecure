# Cart Service API

Web API za upravljanje košaricom artikala. Izgrađen u .NET 10 (ASP.NET Core Minimal API) uz PostgreSQL bazu.

## Brzi start

### Preduvjeti

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/products/docker-desktop)

### Pokretanje (Docker Compose)

```bash
# Pokretanje Postgres baze i API servisa
docker compose up

# Pokretanje Postgres baze i API servisa s inicijalizacijom (briše volume)
docker compose down -v && docker compose up
```

Nakon pokretanja servis je dostupan na:

- API: `http://localhost:8080`
- Swagger UI: `http://localhost:8080/swagger`
- Health check: `http://localhost:8080/health/ready`

### Pokretanje (lokalno, bez Docker Compose)

Ako na računalu postoji PostgreSQL:

```bash
# Postavljanje connection stringa (u appsettings.json ili kroz env varijablu)
# ConnectionStrings__Default=Host=localhost;Port=5432;Database=cartdb;Username=cart;Password=cart

# Pokretanje API servisa
dotnet run --project src/CartService.Api
```

Migracije i inicijalni podaci primjenjuju se automatski pri pokretanju.

## Testovi

```bash
# Pokretanje svih testova
dotnet test

# Pokretanje Unit testova (brzi, ne trebaju Docker)
dotnet test --filter "FullyQualifiedName~UnitTests"

# Pokretanje Integracijskih testova (traže Docker)
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

Integracijski testovi kroz Testcontainers dižu pravi PostgreSQL kontejner, pa im Docker mora biti pokrenut.

## API Endpointi

| Metoda | Ruta | Opis |
|---|---|---|
| `POST` | `/carts` | Kreiranje prazne košarice |
| `GET` | `/carts/{cartId}` | Dohvaćanje košarice s linijama |
| `POST` | `/carts/{cartId}/items` | Dodavanje artikla (ili povećanje količine) |
| `PUT` | `/carts/{cartId}/items/{productId}` | Postavljanje apsolutne količine |
| `DELETE` | `/carts/{cartId}/items/{productId}` | Uklanjanje artikla |
| `DELETE` | `/carts/{cartId}/items` | Pražnjenje košarice |
| `GET` | `/health/live` | Liveness probe |
| `GET` | `/health/ready` | Readiness probe (uključuje bazu) |

### Primjeri

```bash
# Kreiranje košarice
curl -X POST http://localhost:8080/carts

# Dodavanje artikla (ID proizvoda: ca23a19d-7a8b-4e5f-9c1d-000000000001)
curl -X POST http://localhost:8080/carts/{cartId}/items \
  -H "Content-Type: application/json" \
  -d '{"productId": "ca23a19d-7a8b-4e5f-9c1d-000000000001", "quantity": 2}'

# Dohvaćanje košarice
curl http://localhost:8080/carts/{cartId}
```

## Arhitektura

```
CartService.slnx
├── src/
│   ├── CartService.Api/            # Minimal API endpoints, DI, health, Problem Details
│   ├── CartService.Domain/         # Entiteti, domenska pravila, IProductCatalogReader
│   └── CartService.Infrastructure/ # EF DbContext, migracije, PostgresProductCatalogReader
├── tests/
│   ├── CartService.UnitTests/         # MSTest, domenska pravila (in-memory fake)
│   └── CartService.IntegrationTests/  # MSTest, WebApplicationFactory + Testcontainers
├── docker-compose.yml
├── Dockerfile
└── docs/                           # ARCHITECTURE_DESIGN.md, CART_SPEC.md, ADR.md
```

### Ključne odluke

Cijena artikla zamrzava se u trenutku dodavanja u košaricu i kasnije se ne mijenja, čak i ako se cijena u katalogu naknadno promijeni. Konkurentne izmjene iste košarice rješava optimistic concurrency preko Postgresovog `xmin` stupca, pa drugi paralelni zapis dobije `409`.

Sve greške vraćaju se u RFC 7807 (Problem Details) formatu, tako da je odgovor kod grešaka dosljedan kroz cijeli servis.

Autentifikacija stoji iza feature flaga i po defaultu je isključena, kako bi se demo mogao pokrenuti bez postavljanja identity providera. Kad se uključi, servis radi dvije provjere. Scope (`cart:read` / `cart:write`) određuje smije li se pozvati endpoint, a vlasništvo (`Cart.OwnerId` naspram `sub` iz tokena) smije li se dirati baš ta košarica; tuđa vraća `403`. Anonimne (guest) košarice nemaju vlasnika, pa ih štiti jedino nepogodivost GUID-a. Širi kontekst i rubni sloj (gateway, BFF) opisani su u [`docs/ARCHITECTURE_DESIGN.md`](./docs/ARCHITECTURE_DESIGN.md).

Svaki zahtjev nosi correlation ID (W3C Trace Context ili `X-Correlation-ID`), a integracijski testovi rade protiv pravog Postgresa kroz Testcontainers, a ne preko EF In-Memory providera.

## Konfiguracija

| Ključ | Opis | Default |
|---|---|---|
| `ConnectionStrings:Default` | PostgreSQL connection string | `Host=localhost;Port=5432;Database=cartdb;Username=cart;Password=cart` |
| `Auth:Enabled` | Uključuje JWT bearer validaciju | `false` |
| `Auth:Authority` | JWT authority (Keycloak) | `http://localhost:8081/realms/retail` |
| `Auth:Audience` | JWT audience | `cart-service` |

## Dokumentacija

- [`docs/ARCHITECTURE_DESIGN.md`](./docs/ARCHITECTURE_DESIGN.md): high-level dizajn platforme (topologija timova, arhitektura, skaliranje, sigurnost, rubni sloj s BFF-om, vlasništvo košarice, CI/CD)
- [`docs/CART_SPEC.md`](./docs/CART_SPEC.md): implementacijska specifikacija Cart servisa
- [`docs/ADR.md`](./docs/ADR.md): zapisi arhitektonskih odluka (Architecture Decision Records)

## Razvojni pristup

Servis je rađen spec-first. Prvo su nastali vizija ([`docs/ARCHITECTURE_DESIGN.md`](./docs/ARCHITECTURE_DESIGN.md)) i arhitektonske odluke ([`docs/ADR.md`](./docs/ADR.md)), zatim izvršna specifikacija s kriterijima prihvaćanja ([`docs/CART_SPEC.md`](./docs/CART_SPEC.md), sekcija 12), a tek onda implementacija koja se u commitovima poziva na pojedine sekcije specifikacije. Git povijest prati tu slojevitost (dizajn, spec, domena, perzistencija, API, testovi, Docker) umjesto jednog završnog commita.
