# Architecture Decision Records

- **Autor:** Slaven Robić
- **Datum:** 6.6.2026.

Zapis ključnih odluka u formatu: kontekst, razmotrene alternative, odluka, posljedice. Svaka netrivijalna odluka ima zapisan razlog, dostupan i onima koji nisu bili na raspravi.

## ADR-001: Stil komunikacije, event-driven (hibridno)

**Status:** Prihvaćeno

**Kontekst.** Platforma poslužuje milijune korisnika kroz više kanala (web, mobile, marketplace, B2B), uz zahtjeve za real-time obradom i visok promet. Na sustavu rade dva tima koja trebaju isporučivati nezavisno (Sekcija 2). Treba odabrati kako servisi međusobno komuniciraju.

**Razmotrene alternative.**

- **Monolit:** jedna aplikacija/baza, sve sinkrono, ACID besplatno. Odbačeno: onemogućuje nezavisnu isporuku po timu i nezavisno skaliranje vrućih putanja; na ovom volumenu postaje usko grlo. ("Monolith first" je legitiman za jedan tim ili neizvjesnu domenu, ovdje ne vrijedi.)
- **Modularni monolit:** jedan deployable s čistim modulima. Odbačeno kao primarni model: i dalje jedan zajednički release za dva tima; ograničeno nezavisno skaliranje.
- **Servisi sa sinkronom komunikacijom (REST/gRPC):** iste granice servisa, ali direktni sync pozivi. Odbačeno kao glavni obrazac: temporal coupling (pad jednog servisa kaskadno ruši lanac), slabije podnošenje špica, čvršće spajanje timova.
- **Event sourcing + CQRS posvuda:** stanje se rekonstruira iz event loga. Odbačeno: vrlo visoka složenost i eventual consistency svugdje; zahtjevi ne opravdavaju audit/temporalnu vrijednost na razini cijelog sustava (vidi i Sekciju 3.6).

**Odluka.** Event-driven arhitektura, ali hibridno: sinkrono (contract-first REST) gdje je prirodno zahtjev-odgovor (npr. Cart čita Catalog po ID-u), a asinkroni domenski eventi (RabbitMQ) za međudomenske tokove gdje decoupling nosi vrijednost (Order, Payment, Inventory, marketplace sync). Bez event sourcinga kao defaulta.

**Posljedice.**

- Pozitivno: decoupling i nezavisna isporuka dva tima; otpornost (broker apsorbira kvarove i špice); real-time propagacija; nezavisno skaliranje servisa.
- Negativno (i kako se adresira): eventual consistency, rješava saga s kompenzacijama (Sekcija 3.7); dual-write, rješava outbox (3.6); teže debugiranje, rješava korelacijski ID + distribuirani tracing (3.7, 8); pad poruka, rješava retry + DLQ (3.6).
- Granica: napredni obrasci (saga, event sourcing) uvode se tek kad konkretan tok to zatraži. Outbox i DLQ su v1 (nužni), saga kad se pojavi prvi višekoračni tok, event sourcing/Kafka tek uz tvrd zahtjev za povijesnim replayem.

## ADR-002: Granularnost, manji broj servisa po domeni/timu

**Status:** Prihvaćeno

**Kontekst.** Treba odabrati granularnost servisa za platformu s milijunima korisnika i dva tima koja isporučuju nezavisno.

**Razmotrene alternative.**

- **Jedan monolit:** najjednostavnije, ACID besplatno. Odbačeno: nema nezavisne isporuke po timu ni nezavisnog skaliranja vrućih putanja; usko grlo na ovom volumenu.
- **Fino zrnati mikroservisi (20+):** maksimalna nezavisnost. Odbačeno: dva tima ne mogu operativno održavati toliko servisa; prerana fragmentacija donosi distribuiranu složenost bez koristi.

**Odluka.** Manji broj servisa poravnatih s domenama i vlasništvom timova (Storefront: Catalog, Cart; Commerce: Checkout, Orders, Payments, Inventory, Pricing, Invoicing, Integrations), uz modularnu disciplinu unutar svakog servisa. Granularnost se mijenja evolucijski, tek kad stvarni pokretač to opravda.

**Posljedice.** Pozitivno: nezavisna isporuka i skaliranje po domeni; granice se poklapaju s vlasništvom timova (Conway). Negativno: distribuirana složenost (adresirana u 3.6/3.7). Granica: daljnja podjela servisa samo uz konkretan pokretač (skaliranje/autonomija).

## ADR-003: Primarna baza, PostgreSQL + database-per-service

**Status:** Prihvaćeno

**Kontekst.** Treba odabrati primarni transakcijski store. Sustav je cloud-agnostičan i koristi .NET 10/EF Core.

**Razmotrene alternative.**

- **Jedna dijeljena baza za sve servise:** jednostavnija operativno. Odbačeno: skriveno spajanje kroz shemu vodi u "distribuirani monolit", gubi se nezavisna deployabilnost.
- **NoSQL kao primarni store:** fleksibilna shema. Odbačeno za transakcijsku jezgru: commerce traži jake transakcije i relacijski integritet; NoSQL se koristi ciljano (npr. ES kao read model, Sekcija 3.3).

**Odluka.** PostgreSQL kao primarni store, database-per-service (svaki servis vlasnik svoje sheme). EF Core 10 kao ORM. Cloud-agnostično, dokazano za visok promet.

**Posljedice.** Pozitivno: jake transakcije, relacijski integritet, nezavisno vlasništvo podataka. Negativno: nema upita preko servisa kroz bazu, razmjena samo preko evenata/kontrakata (po dizajnu). Skaliranje: read replicas i particioniranje kad volumen traži (Sekcija 5.4).

## ADR-004: Message broker, RabbitMQ

**Status:** Prihvaćeno

**Kontekst.** Async komunikacija među servisima (Sekcija 3.3). Treba odabrati broker; jedan od zahtjeva je real-time obrada.

**Razmotrene alternative.**

- **Kafka:** event-log s retentionom, replay, visok throughput. Odbačeno kao default: veći operativni teret; event-log semantika (replay povijesti) nije v1 zahtjev.
- **Azure Service Bus / cloud-managed:** nizak operativni teret. Odbačeno: kosi se s cloud-agnostičnom odlukom (Sekcija 3.4).

**Odluka.** RabbitMQ, pouzdan async pub/sub uz nizak operativni teret, cloud-agnostičan. Pouzdanost kroz retry + DLQ + outbox (Sekcija 3.6).

**Posljedice.** Pozitivno: jednostavnost, nizak teret, pokriva real-time pub/sub. Negativno: nije log, pa nema besplatnog povijesnog replaya; rješava se outboxom i DLQ replayem. Okidač za promjenu: tvrd zahtjev za arbitrarnim povijesnim replayem ili event sourcingom vodi na uvođenje Kafke.

## ADR-005: Identitet, "buy, not build" (Keycloak)

**Status:** Prihvaćeno

**Kontekst.** Sustav treba autentifikaciju/autorizaciju za web, mobile i B2B (Sekcija 6), uz naglašen zahtjev za sigurnošću.

**Razmotrene alternative.**

- **Vlastiti identity servis (.NET):** puna kontrola. Odbačeno: identitet je sigurnosno-kritična, riješena commodity domena; custom rješenje znači preuzeti rizik (hashing, token rotacija, key rollover, CVE održavanje) bez diferencijacije.
- **Cloud-managed IdP (Entra ID / Auth0):** najmanji teret. Odbačeno kao default: vendor lock-in i kosi se s cloud-agnostičnom linijom.

**Odluka.** Keycloak (self-hosted) kao IdP, uz standardni OIDC/OAuth2. Standard je fiksan, provider zamjenjiv. Inženjerski trud ide u commerce domenu (diferencijator), ne u auth server.

**Posljedice.** Pozitivno: dokazani IdP bez sigurnosnog rizika custom rješenja; standard čini provider zamjenjivim. Negativno: operativni teret održavanja Keycloaka. Servisi: svaki je resource server (validacija + policy authorization), izdavanje identiteta je IdP-ova odgovornost.

## ADR-006: Dokumenti (PDF računi) u object storage

**Status:** Prihvaćeno

**Kontekst.** Invoicing generira PDF račune koje treba trajno čuvati (zakonska obveza) i posluživati (Sekcija 7).

**Razmotrene alternative.**

- **Binarni sadržaj u relacijskoj bazi (BLOB):** sve na jednom mjestu. Odbačeno: napuhava bazu i backupe, skupo skalira, nije CDN-friendly.

**Odluka.** Object storage (S3-kompatibilan) za sadržaj, metapodaci u Postgresu (`invoice_id`, `order_id`, storage ključ, hash, status). Pristup preko pre-signed URL-ova s ograničenim trajanjem.

**Posljedice.** Pozitivno: jeftino, skalabilno, CDN-friendly; baza ostaje vitka. Negativno: dva sustava za održavati (store + metapodaci). Sigurnost: računi su immutable (korekcija je nova storno faktura), write-once uz retention zbog zakonske obveze čuvanja.

## ADR-007: Observability backend, self-hosted uz managed kao opciju

**Status:** Prihvaćeno

**Kontekst.** Treba odabrati gdje se telemetrija sprema, vizualizira i alarmira. Instrumentacija je standardizirana na OpenTelemetry (Sekcija 8.1), pa je backend odvojiv. Sustav je cloud-agnostičan i izbjegava vendor lock-in (Sekcija 3.4), ali observabilnost na razini milijuna korisnika nosi realan operativni teret.

**Razmotrene alternative.**

- **Self-hosted (Prometheus + Grafana + Loki/Tempo):** open-source, bez lock-ina, podaci kod nas. Cijena: sami održavamo i skaliramo observability infrastrukturu.
- **Managed SaaS (New Relic, Datadog, Grafana Cloud):** nizak operativni teret, sve-u-jednom. Cijena: plaćanje po podacima/korisnicima (na ovoj skali potencijalno vrlo skupo), vendor lock-in, telemetrija izvan naše infrastrukture (GDPR/data residency razmatranje).

**Odluka.** Self-hosted Prometheus/Grafana kao default, dosljedno s cloud-agnostičnom i lock-in-averznom linijom. Budući da svi servisi šalju OpenTelemetry, backend je zamjenjiv: managed platforma (New Relic/Datadog/Grafana Cloud) je drop-in alternativa ako operativni teret nadmaši vrijednost.

**Posljedice.**

- Pozitivno: nema vendor lock-ina, podaci kod nas, predvidiv trošak infrastrukture; opcija ostaje otvorena zahvaljujući OTel apstrakciji.
- Negativno: preuzimamo operativni teret održavanja i skaliranja observability stacka; tim mora imati to znanje.
- Okidač za promjenu: ako održavanje observabilnosti počne trošiti nesrazmjeran kapacitet timova, prelazak na managed je jeftin jer instrumentacija ostaje ista.
