# Cart Service API

Web API za upravljanje košaricom artikala — .NET 10, ASP.NET Core Minimal API, PostgreSQL.

## Brzi start

### Preduvjeti

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/products/docker-desktop)

### Pokretanje (Docker Compose)

```bash
# 1. Diži Postgres + API
docker compose up

# Čist start od nule (briše volume):
docker compose down -v && docker compose up
```

API je dostupan na:
- **API:** `http://localhost:8080`
- **Swagger UI:** `http://localhost:8080/swagger`
- **Health:** `http://localhost:8080/health/ready`

### Pokretanje (lokalno, bez Docker Compose)

Ako već imaš PostgreSQL:

```bash
# 1. Postavi connection string u appsettings.json ili preko env var
# ConnectionStrings__Default=Host=localhost;Port=5432;Database=cartdb;Username=cart;Password=cart

# 2. Pokreni API
dotnet run --project src/CartService.Api
```

Migracije i seed proizvoda pokreću se automatski na startupu.

## Testovi

```bash
# Svi testovi (unit + integration)
dotnet test

# Samo unit testovi (brzi, bez Docker-a)
dotnet test --filter "FullyQualifiedName~UnitTests"

# Samo integration testovi (zahtijevaju Docker)
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

> **Napomena:** Integration testovi koriste Testcontainers za dizanje pravog PostgreSQL kontejnera.

## API Endpointi

| Metoda | Ruta | Opis |
|---|---|---|
| `POST` | `/carts` | Kreira praznu košaricu |
| `GET` | `/carts/{cartId}` | Dohvat košarice s linijama |
| `POST` | `/carts/{cartId}/items` | Dodaje artikl (ili povećava količinu) |
| `PUT` | `/carts/{cartId}/items/{productId}` | Postavlja apsolutnu količinu |
| `DELETE` | `/carts/{cartId}/items/{productId}` | Uklanja artikl |
| `DELETE` | `/carts/{cartId}/items` | Prazni košaricu |
| `GET` | `/health/live` | Liveness probe |
| `GET` | `/health/ready` | Readiness probe (uklj. bazu) |

### Primjeri

```bash
# Kreiraj košaricu
curl -X POST http://localhost:8080/carts

# Dodaj artikl (proizvod ID: ca23a19d-7a8b-4e5f-9c1d-000000000001)
curl -X POST http://localhost:8080/carts/{cartId}/items \
  -H "Content-Type: application/json" \
  -d '{"productId": "ca23a19d-7a8b-4e5f-9c1d-000000000001", "quantity": 2}'

# Dohvati košaricu
curl http://localhost:8080/carts/{cartId}
```

## Arhitektura

```
CartService.slnx
├── src/
│   ├── CartService.Api/           # Minimal API endpoints, DI, health, Problem Details
│   ├── CartService.Domain/        # Entiteti, domenska pravila, IProductCatalogReader
│   └── CartService.Infrastructure/ # EF DbContext, migracije, PostgresProductCatalogReader
├── tests/
│   ├── CartService.UnitTests/     # MSTest — domenska pravila (in-memory fake)
│   └── CartService.IntegrationTests/ # MSTest — WebApplicationFactory + Testcontainers
├── docker-compose.yml
├── Dockerfile
└── docs/                          # ARCHITECTURE_DESIGN.md, CART_SPEC.md, ADR.md
```

### Ključne odluke

- **Snapshot cijene:** cijena se "zamrzava" pri dodavanju u košaricu i ne mijenja se kasnije.
- **Optimistic concurrency:** preko Postgres `xmin` stupca, vraća `409` na konflikt.
- **Problem Details (RFC 7807):** dosljedan format grešaka.
- **Feature-flagged auth:** JWT bearer placeholder (default isključen za demo). Kad je uključen, radi dvije provjere: **scope** (`cart:read`/`cart:write`) kontrolira pristup endpointu, a **ownership** (`Cart.OwnerId` vs `sub` iz tokena) pristup podatku — `403` za tuđu košaricu. Guest košarice (`OwnerId = null`) štiti nepogodivost GUID-a. Detalji i rubni sloj (gateway + BFF) u [`docs/ARCHITECTURE_DESIGN.md`](./docs/ARCHITECTURE_DESIGN.md).
- **Correlation ID:** middleware za W3C Trace Context / `X-Correlation-ID`.
- **Testcontainers:** integration testovi protiv pravog Postgresa (ne EF In-Memory).

## Konfiguracija

| Ključ | Opis | Default |
|---|---|---|
| `ConnectionStrings:Default` | PostgreSQL connection string | `Host=localhost;Port=5432;Database=cartdb;Username=cart;Password=cart` |
| `Auth:Enabled` | Uključuje JWT bearer validaciju | `false` |
| `Auth:Authority` | JWT authority (Keycloak) | `http://localhost:8081/realms/retail` |
| `Auth:Audience` | JWT audience | `cart-service` |

## Dokumentacija

- [`docs/ARCHITECTURE_DESIGN.md`](./docs/ARCHITECTURE_DESIGN.md) — High-level dizajn platforme: topologija timova, arhitektura, skaliranje, sigurnost, rubni sloj (BFF) i vlasništvo košarice, CI/CD
- [`docs/CART_SPEC.md`](./docs/CART_SPEC.md) — Implementacijska specifikacija Cart servisa (što se točno gradi)
- [`docs/ADR.md`](./docs/ADR.md) — Architecture Decision Records (kontekst → alternative → odluka → posljedice)

## Razvojni pristup

Servis je razvijan **spec-first**: prvo su nastali vizija ([`docs/ARCHITECTURE_DESIGN.md`](./docs/ARCHITECTURE_DESIGN.md)) i odluke ([`docs/ADR.md`](./docs/ADR.md)), zatim izvršna specifikacija s acceptance kriterijima ([`docs/CART_SPEC.md`](./docs/CART_SPEC.md) §12), pa implementacija koja se u commitovima referencira na sekcije spec-a. Git povijest odražava tu slojevitu strukturu (dizajn → spec → domena → perzistencija → API → testovi → Docker), a ne jedan "final" commit.
