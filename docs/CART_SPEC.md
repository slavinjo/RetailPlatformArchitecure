# Cart Web API — Implementacijska specifikacija (`CART_SPEC.md`)

**Autor:** Slaven Robić · **Datum:** 2026-07-05

> **Svrha dokumenta:** precizna, izvršna specifikacija referentne implementacije (Cart Web API) iz zadatka. Za razliku od [`ARCHITECTURE_DESIGN.md`](./ARCHITECTURE_DESIGN.md) koji opisuje *viziju i obrazloženje*, ovaj dokument opisuje *točno što se gradi* — bez naracije, tako da se iz njega može implementirati bez nagađanja.
>
> **Opseg (scope):** jedan Web API servis za košaricu artikala, s bazom podataka i odabranim podskupom zahtjeva iz zadatka (perzistencija, validacija, healthcheck, kontejnerizacija, testovi).

---

## 1. Opseg i ne-ciljevi

### 1.1 U opsegu (v1)

- Web API za upravljanje košaricom: kreiranje, dohvat, dodavanje/ažuriranje/uklanjanje artikala, pražnjenje.
- Perzistencija u **PostgreSQL** preko **EF Core 10**.
- Minimalni **katalog proizvoda** (`products` tablica) kao izvor za validaciju i cijenu — pristupan preko apstrakcije `IProductCatalogReader`.
- **Snapshot cijene** pri dodavanju artikla u košaricu.
- **Optimistic concurrency** na košarici.
- **Healthcheck** endpointi (liveness/readiness).
- **Docker Compose** za lokalno pokretanje (API + Postgres).
- **Optional policy-based authorization** (resource-server placeholder, feature-flagged) — demonstrira obrazac bez stvarnog IdP-a.
- **Testovi**: unit (domenska pravila) + nekoliko integration testova.

### 1.2 Ne-ciljevi (svjesno izvan v1)

Ovo NIJE u opsegu i namjerno je izostavljeno (da bi opseg ostao fokusiran):

- ❌ Plaćanje / checkout / narudžbe — zasebna domena (Team B).
- ❌ **Pun IdP / upravljanje korisnicima** (registracija, login, password reset, izdavanje tokena, Keycloak) — u produkciji preko gatewaya/Keycloaka (vidi `ARCHITECTURE_DESIGN.md` §6). Demo prikazuje samo **resource-server stranu** (validacija tokena + provjera scopea), i to feature-flagged (vidi §6.5).
- ❌ Full-text search / Elasticsearch — košarica koristi samo dohvat po ID-u; search je dio Catalog/Search domene (vidi `ARCHITECTURE_DESIGN.md` §3).
- ❌ Upravljanje katalogom (CRUD proizvoda) — `products` se samo seeda; košarica ga čita, ne upravlja njime.

> Ne-ciljevi su eksplicitni jer demonstriraju **svjesnu odluku o opsegu**, a ne propust.

---

## 2. Tehnološki stack

| Sloj | Tehnologija | Verzija / napomena |
|---|---|---|
| Runtime / jezik | .NET / C# | **.NET 10 (LTS)**, C# 14 |
| Web | ASP.NET Core **Minimal API** | OpenAPI omogućen |
| ORM | **EF Core 10** | Provider: Npgsql |
| Baza | **PostgreSQL** | 17 (preko Docker Compose) |
| Kontejnerizacija | Docker + Docker Compose | named volume, healthcheck |
| Test framework | **MSTest** | — |
| Test assertions | **FluentAssertions** | čitljive tvrdnje |
| Integration host | `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) | — |
| Integration baza | **Testcontainers for .NET** (PostgreSQL modul) | pravi Postgres po test runu |
| Healthchecks | `Microsoft.Extensions.Diagnostics.HealthChecks` | — |
| Auth (placeholder) | `Microsoft.AspNetCore.Authentication.JwtBearer` | resource-server validacija, feature-flagged (§6.5) |

---

## 3. Struktura projekta

Namjerno minimalna, ali slojevita — granica `Domain`↔`Infrastructure` materijalizira apstrakciju kataloga (šav prema Catalog domeni iz `ARCHITECTURE_DESIGN.md` §2).

```
CartService.slnx
├── src/
│   ├── CartService.Api/              # Minimal API endpoints, Program.cs, DI, health, Problem Details
│   ├── CartService.Domain/           # Entiteti, domenska pravila, IProductCatalogReader, ProductInfo
│   └── CartService.Infrastructure/   # EF DbContext, migracije, PostgresProductCatalogReader, seed
├── tests/
│   ├── CartService.UnitTests/        # MSTest — domenska pravila (in-memory fake, bez baze)
│   └── CartService.IntegrationTests/ # MSTest — WebApplicationFactory + Testcontainers Postgres
├── docker-compose.yml
├── Dockerfile
├── README.md
└── docs/
    ├── ARCHITECTURE_DESIGN.md                     # Vizija i high-level arhitektura platforme
    ├── CART_SPEC.md                  # Ovaj dokument — implementacijska specifikacija
    └── ADR.md                        # Architecture Decision Records
```

> **Napomena o slojevitosti:** za jedan servis ovo je gornja granica razumne strukture — ne ide se u punu Clean Architecture s 6 projekata. `Domain` ne ovisi ni o čemu; `Infrastructure` ovisi o `Domain`; `Api` ih spaja.

---

## 4. Model podataka

### 4.1 Entiteti

**`Cart`**

| Polje | Tip | Napomena |
|---|---|---|
| `Id` | `Guid` | PK |
| `CreatedAt` | `DateTimeOffset` | UTC |
| `UpdatedAt` | `DateTimeOffset` | UTC, ažurira se pri svakoj promjeni |
| `Items` | `List<CartItem>` | navigacijska kolekcija |
| *(concurrency)* | `xmin` | Postgres sistemski stupac, mapiran kao concurrency token (vidi §7.2) |

**`CartItem`**

| Polje | Tip | Napomena |
|---|---|---|
| `Id` | `Guid` | PK |
| `CartId` | `Guid` | FK → `Cart.Id` |
| `ProductId` | `Guid` | referenca na proizvod iz kataloga |
| `ProductName` | `string` | **snapshot** imena u trenutku dodavanja |
| `UnitPrice` | `decimal(18,2)` | **snapshot** cijene u trenutku dodavanja |
| `Quantity` | `int` | > 0 |
| `AddedAt` | `DateTimeOffset` | UTC |

**`Product`** (katalog — samo za čitanje iz perspektive košarice)

| Polje | Tip | Napomena |
|---|---|---|
| `Id` | `Guid` | PK |
| `Name` | `string` | — |
| `UnitPrice` | `decimal(18,2)` | trenutna cijena u katalogu |
| `IsAvailable` | `bool` | je li proizvod dostupan za prodaju |

### 4.2 Pravila modela

- `CartItem` je jedinstven po `(CartId, ProductId)` — isti proizvod ne stvara dvije linije (vidi §6.1).
- `decimal(18,2)` za sve novčane iznose — eksplicitna preciznost (ne `float`/`double`).
- Sva vremena u **UTC** (`DateTimeOffset`).
- Brisanje `Cart`-a kaskadno briše `CartItem`-e.

---

## 5. API kontrakt

Bazni put: `/carts`. Format greške: **RFC 7807 Problem Details** (vidi §8). Svi `4xx`/`5xx` vraćaju `application/problem+json`.

| # | Metoda | Ruta | Opis | Uspjeh | Greške |
|---|---|---|---|---|---|
| 1 | `POST` | `/carts` | Kreira praznu košaricu | `201 Created` + `CartResponse` | — |
| 2 | `GET` | `/carts/{cartId}` | Dohvat košarice s linijama i ukupnim iznosom | `200 OK` + `CartResponse` | `404` (košarica ne postoji) |
| 3 | `POST` | `/carts/{cartId}/items` | Dodaje artikl (ili povećava količinu) | `200 OK` + `CartResponse` | `404` (košarica), `422` (proizvod ne postoji/nedostupan, količina ≤ 0) |
| 4 | `PUT` | `/carts/{cartId}/items/{productId}` | Postavlja apsolutnu količinu artikla | `200 OK` + `CartResponse` | `404` (košarica/linija), `422` (količina ≤ 0) |
| 5 | `DELETE` | `/carts/{cartId}/items/{productId}` | Uklanja artikl iz košarice | `204 No Content` | `404` (košarica/linija) |
| 6 | `DELETE` | `/carts/{cartId}/items` | Prazni košaricu | `204 No Content` | `404` (košarica) |
| 7 | `GET` | `/health/live` | Liveness probe | `200 OK` | `503` |
| 8 | `GET` | `/health/ready` | Readiness probe (uklj. bazu) | `200 OK` | `503` |

> Kad je `Auth:Enabled=true` (§6.5), svi `/carts` endpointi (#1–#6) dodatno mogu vratiti `401` (nema/nevažeći token) i `403` (nedostaje scope ili košarica pripada drugom vlasniku).

### 5.1 Request sheme

**Add item (#3)** — `POST /carts/{cartId}/items`
```json
{ "productId": "guid", "quantity": 2 }
```

**Update quantity (#4)** — `PUT /carts/{cartId}/items/{productId}`
```json
{ "quantity": 5 }
```

### 5.2 Response shema

**`CartResponse`**
```json
{
  "cartId": "guid",
  "items": [
    {
      "productId": "guid",
      "productName": "Primjer proizvod",
      "unitPrice": 19.99,
      "quantity": 2,
      "lineTotal": 39.98
    }
  ],
  "totalItems": 2,
  "totalAmount": 39.98,
  "createdAt": "2026-06-30T10:00:00Z",
  "updatedAt": "2026-06-30T10:05:00Z"
}
```

- `lineTotal = unitPrice * quantity` (računato, ne sprema se).
- `totalItems` = zbroj svih `quantity`.
- `totalAmount` = zbroj svih `lineTotal`.

---

## 6. Domenska pravila i edge-caseovi

### 6.1 Dodavanje artikla (`POST .../items`)

1. Ako košarica ne postoji → `404`.
2. Dohvati proizvod preko `IProductCatalogReader.GetProductAsync(productId)`.
3. Ako proizvod ne postoji → `422` (`product_not_found`).
4. Ako `IsAvailable == false` → `422` (`product_unavailable`).
5. Ako `quantity <= 0` → `422` (`invalid_quantity`).
6. **Snapshot:** preuzmi `Name` i `UnitPrice` iz dohvaćenog proizvoda i spremi ih u liniju.
7. Ako linija s istim `ProductId` već postoji → **povećaj `Quantity`** (merge), ne stvaraj duplikat. *Cijena se NE re-snapshota* — ostaje izvorni snapshot.
8. Ažuriraj `Cart.UpdatedAt`.

### 6.2 Ažuriranje količine (`PUT .../items/{productId}`)

- Postavlja **apsolutnu** količinu (ne inkrement).
- Ako linija ne postoji → `404`.
- Ako `quantity <= 0` → `422`. *(Uklanjanje ide preko `DELETE`, ne preko `PUT` s 0.)*

### 6.3 Snapshot cijene (ključno domensko pravilo)

> Cijena u `CartItem` je **snapshot iz trenutka dodavanja** i ne mijenja se ako se cijena u katalogu kasnije promijeni. Korisnik vidi cijenu koju je vidio kad je dodao artikl. Re-validacija cijena protiv kataloga radi se tek na checkoutu (izvan opsega ovog servisa).

Ovo pravilo MORA biti pokriveno unit testom (§9.1).

### 6.4 Sažetak validacija

| Slučaj | Status | `type` / kod |
|---|---|---|
| Košarica ne postoji | `404` | `cart_not_found` |
| Linija ne postoji (PUT/DELETE) | `404` | `cart_item_not_found` |
| Proizvod ne postoji | `422` | `product_not_found` |
| Proizvod nedostupan | `422` | `product_unavailable` |
| Količina ≤ 0 | `422` | `invalid_quantity` |
| Concurrency konflikt | `409` | `concurrency_conflict` (vidi §7.2) |
| Košarica pripada drugom vlasniku (auth) | `403` | `cart_forbidden` (vidi §6.5) |

### 6.5 Authorization (placeholder, feature-flagged)

Demo prikazuje **resource-server obrazac** iz `ARCHITECTURE_DESIGN.md` §6 — kako servis validira token i provodi policy-based authorization — bez dizanja stvarnog IdP-a.

- **Feature flag:** `Auth:Enabled` (`appsettings.json`). **Default `false`** → demo se pokreće bez tokena (čuva zero-friction iz §10). Kad je `true`, servis postaje resource server.
- **Kad je uključeno:** JWT bearer validacija (`Authority`/`Audience` iz konfiguracije; u produkciji Keycloak). Endpointi su zaštićeni **policy-based authorization**:
  - `cart:read` → `GET` endpoint (#2)
  - `cart:write` → `POST`/`PUT`/`DELETE` endpointi (#1, #3–#6)
- **Scope vs. podatak:** scope kontrolira *pristup endpointu*; vlasništvo košarice je domenska provjera u servisu. Košarica se pri kreiranju (#1) veže uz subjekt iz tokena (`Cart.OwnerId = sub`); bez tokena ostaje anonimna (`OwnerId = null`, guest). Pristup košarici čiji se `OwnerId` ne poklapa sa `sub`-om pozivatelja → `403` (`cart_forbidden`). Demonstrira načelo "scope ne zamjenjuje domensku autorizaciju" (`ARCHITECTURE_DESIGN.md` §6.6).
- **Statusi kad je uključeno:** `401` (nema/nevažeći token), `403` (nedostaje scope *ili* tuđa košarica — `cart_forbidden`) — u RFC 7807 formatu (§8).
- **Zašto feature flag, a ne uvijek-uključeno:** ocjenjivač mora moći pokrenuti demo bez postavljanja IdP-a; flag pokazuje da znaš resource-server obrazac, a ne nameće infrastrukturu. Realni IdP je svjesni ne-cilj (§1.2).

> **Napomena:** ovo je *placeholder* — pokazuje mehaniku (JWT validacija + policy authorization), ne pun auth sustav. Izdavanje tokena, korisnici i flow-ovi su odgovornost IdP-a u produkciji.

### 6.6 Korelacijski ID i strukturirano logiranje

Demo pokazuje **traceability obrazac** iz `ARCHITECTURE_DESIGN.md` §3.7 — jeftino, bez vanjske infrastrukture:

- **Correlation middleware:** na svaki zahtjev čita `traceparent` (W3C Trace Context) ili `X-Correlation-ID`; ako nedostaje, generira novi `Guid`. ID se vraća u response zaglavlju.
- **Strukturirano logiranje:** svaka log linija nosi `correlationId`/`traceId` (npr. preko Serilog enrichera ili `ILogger` scope-a). Time se **po jednom ID-u prati cijeli zahtjev** — temelj za pretragu logova kad servis postane dio šireg toka.
- **Veza s idempotentnošću:** correlation ID prati tok; ako Cart u budućnosti počne objavljivati/konzumirati evente, deduplikacija ide po zasebnom **message ID-u** poruke (inbox pattern, `ARCHITECTURE_DESIGN.md` §3.6–3.7), ne po correlation ID-u.

> Ovo je čista mehanika (middleware + logging scope) — ne zahtijeva broker ni tracing backend u demu, ali demonstrira da znaš provući korelacijski ID kroz servis.

---

## 7. Perzistencija

### 7.1 EF Core

- `CartDbContext` s `DbSet<Cart>`, `DbSet<CartItem>`, `DbSet<Product>`.
- Konfiguracija preko `IEntityTypeConfiguration<T>` (ne data annotations).
- `decimal` precizno mapiran na `numeric(18,2)`.
- **Migracije** generirane EF alatom; primjenjuju se na startupu (§7.3).

### 7.2 Optimistic concurrency

- Na `Cart` se mapira Postgres sistemski stupac **`xmin`** kao concurrency token:
  ```csharp
  builder.Property<uint>("xmin").IsRowVersion();
  ```
- Pri konkurentnoj izmjeni iste košarice, drugi `SaveChanges` baca `DbUpdateConcurrencyException` → API vraća `409 Conflict` (`concurrency_conflict`).
- Ovo demonstrira ispravno rukovanje konkurentnim izmjenama košarice (npr. dva taba istog korisnika).

### 7.3 Migracije i seed na startupu

Na pokretanju aplikacije:
1. `db.Database.Migrate()` — primijeni sve migracije (kreira shemu ako ne postoji).
2. **Seed** — ako je `products` prazna, ubaci nekoliko proizvoda (npr. 5), uključujući barem jedan s `IsAvailable = false` (za testiranje pravila nedostupnosti).

> Time i prazna i postojeća baza završe u poznatom, ispravnom stanju — deterministički demo.

---

## 8. Rukovanje greškama (Problem Details)

- Sve greške vraćaju **RFC 7807** `application/problem+json` preko ASP.NET Core `ProblemDetails` infrastrukture.
- Globalni exception handler (`IExceptionHandler`) mapira domenske iznimke na odgovarajuće statuse.
- Primjer:
```json
{
  "type": "https://cartservice/errors/product_unavailable",
  "title": "Product is not available",
  "status": 422,
  "detail": "Product 'a1b2...' is not available for purchase.",
  "instance": "/carts/{cartId}/items"
}
```
- Jedinstven, dosljedan format greške je standard koji se (u širem sustavu) dijeli među timovima (`ARCHITECTURE_DESIGN.md` §2.5).

---

## 9. Testovi

### 9.1 Unit testovi (MSTest + FluentAssertions, bez baze)

Testiraju **domenska pravila** koristeći **in-memory `IProductCatalogReader`** (`InMemoryProductCatalogReader`). Glavnina testova. Pokrivaju barem:

- Dodavanje nepostojećeg proizvoda → odbijeno (`product_not_found`).
- Dodavanje nedostupnog proizvoda → odbijeno (`product_unavailable`).
- Količina ≤ 0 → odbijeno (`invalid_quantity`).
- **Snapshot cijene** — promjena cijene u katalogu nakon dodavanja NE mijenja cijenu u liniji.
- Dodavanje istog proizvoda dvaput → jedna linija, zbrojena količina.
- Ažuriranje količine na apsolutnu vrijednost.
- Uklanjanje linije i pražnjenje košarice.
- Izračun `lineTotal` / `totalAmount` / `totalItems`.

Imenovanje: `Method_Scenario_ExpectedResult` (npr. `AddItem_WhenProductUnavailable_IsRejected`). Struktura: Arrange-Act-Assert.

### 9.2 Integration testovi (MSTest + WebApplicationFactory + Testcontainers Postgres)

Nekoliko ključnih (5–8), protiv **pravog Postgresa** dignutog preko Testcontainersa:

- `POST /carts` → `201` + valjan `cartId`.
- Pun tijek: kreiraj → dodaj artikl → dohvati → ukloni → potvrdi prazno.
- Dodavanje u nepostojeću košaricu → `404` s ispravnim Problem Details.
- Dodavanje nepostojećeg/nedostupnog proizvoda → `422`.
- **Optimistic concurrency** — dvije paralelne izmjene iste košarice; jedna uspije, druga dobije `409`.
- Perzistencija — podaci prežive ponovni dohvat.

> **Zašto Testcontainers, a ne EF In-Memory:** EF In-Memory ne reproducira transakcije, constrainte, `decimal` ponašanje ni concurrency. Integration testovi protiv pravog Postgresa testiraju ono što se stvarno koristi u produkciji.

### 9.3 Što se NE testira

- ❌ EF Core, getteri/setteri, framework kod.
- ❌ Ciljanje 100% line coverage — cilj je pokrivenost ponašanja.

---

## 10. Lokalno pokretanje (sažeto; puni vodič u `README.md`)

```bash
# 1. Diži Postgres + API
docker compose up        # podaci perzistiraju (named volume)

# Čist start od nule:
docker compose down -v && docker compose up

# 2. API dostupan na:
#    http://localhost:8080
#    OpenAPI/Swagger: http://localhost:8080/swagger
#    Health: http://localhost:8080/health/ready

# 3. Testovi (zahtijevaju Docker za integration testove):
dotnet test
```

### 10.1 Docker Compose (skica)

```yaml
services:
  db:
    image: postgres:17
    environment:
      POSTGRES_USER: cart
      POSTGRES_PASSWORD: cart
      POSTGRES_DB: cartdb
    ports: ["5432:5432"]
    volumes: ["cart_pgdata:/var/lib/postgresql/data"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U cart -d cartdb"]
      interval: 5s
      timeout: 3s
      retries: 5

  api:
    build: .
    depends_on:
      db:
        condition: service_healthy
    environment:
      ConnectionStrings__Default: "Host=db;Port=5432;Database=cartdb;Username=cart;Password=cart"
    ports: ["8080:8080"]

volumes:
  cart_pgdata:
```

> `depends_on: condition: service_healthy` — API se diže tek kad je baza zdrava (rješava "app pao jer baza nije spremna"). Healthcheck je usput i zahtjev iz zadatka (`ARCHITECTURE_DESIGN.md` §8).

---

## 11. Apstrakcija kataloga (swap-point)

```csharp
// CartService.Domain
public interface IProductCatalogReader
{
    Task<ProductInfo?> GetProductAsync(Guid productId, CancellationToken ct);
}

public sealed record ProductInfo(Guid Id, string Name, decimal UnitPrice, bool IsAvailable);
```

| Implementacija | Gdje | Kad |
|---|---|---|
| `PostgresProductCatalogReader` | `Infrastructure` | **demo/prod v1** — `SELECT` iz `products` |
| `InMemoryProductCatalogReader` | `tests` | **unit testovi** — kontrolirani podaci |
| *(budućnost)* ES / HTTP klijent na Catalog servis | — | **produkcija** — vidi `ARCHITECTURE_DESIGN.md` §3 |

> Interface je **uzak** (samo get-by-id) jer košarici toliko i treba. Time apstrakcija ostaje čista, a izvor podataka kataloga je zamjenjiv bez ikakve izmjene logike košarice.

---

## 12. Acceptance kriteriji (sažetak)

Rješenje je "gotovo" kad:

- [ ] Svih 8 endpointa radi prema §5 s točnim statusnim kodovima.
- [ ] Snapshot cijene se ponaša prema §6.3 (pokriveno testom).
- [ ] Optimistic concurrency vraća `409` na konflikt (pokriveno testom).
- [ ] Sve greške su RFC 7807 Problem Details (§8).
- [ ] `docker compose up` diže API + Postgres; healthcheck zelen.
- [ ] Migracije + seed rade na startupu (§7.3).
- [ ] Authorization placeholder (§6.5): s `Auth:Enabled=false` demo radi bez tokena; s `true` zaštićeni endpointi vraćaju `401`/`403` bez/krivim scopeom.
- [ ] Unit testovi (§9.1) i integration testovi (§9.2) prolaze (`dotnet test`).
- [ ] `README.md` sadrži upute za pokretanje.
- [ ] Commitovi prate razvoj inkrementalno (ne jedan "final" commit).
