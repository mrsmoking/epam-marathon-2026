# Restaurant AQA framework

End-to-end tests for the Green & Tasty restaurant application: Reqnroll (Gherkin) + TUnit + Playwright for .NET, with the Screenplay pattern.

Guidance for extending the suite (writing Gherkin, steps, Screenplay, test data, working with an AI assistant) is in [CLAUDE.md](CLAUDE.md).

## Рішення домашнього завдання (Test Automation)

**Напрям: 1. Груповий pre-order.** Тестується власна реалізація: Backend і Frontend групового pre-order (рівні 1–3, включно з бонусом Remove Guest), інтегровані й розгорнуті в AWS.

**Підхід: BDD + Screenplay**, як у наданому фреймворку. Gherkin (Reqnroll) описує поведінку бізнес-мовою. Кроки — тонка склейка з актором. Актор виконує задачі (Tasks) і ставить питання (Questions) через API-клієнти та page objects.

### Що покрито

| Рівень | Фіча | Стадія | Сценаріїв |
|---|---|---|---|
| L1 — перегляд учасників | [GroupPreOrderParticipants.feature](Features/GroupPreOrderParticipants.feature) | `@api` | 9 |
| | [GroupPreOrderScreen.feature](Features/GroupPreOrderScreen.feature) | `@ui` | 5 |
| L2 — підтвердження | [GroupPreOrderSubmit.feature](Features/GroupPreOrderSubmit.feature) | `@api` | 12 |
| | [GroupPreOrderSubmitScreen.feature](Features/GroupPreOrderSubmitScreen.feature) | `@ui` | 4 |
| L3 — видалення учасника | [GroupPreOrderRemoval.feature](Features/GroupPreOrderRemoval.feature) | `@api` | 11 |
| | [GroupPreOrderRemovalScreen.feature](Features/GroupPreOrderRemovalScreen.feature) (включно з наскрізним `@e2e`) | `@ui` | 2 |

Піраміда тестів:
- **UI** — основні сценарії: Owner бачить учасників, страви й суми; підтверджує pre-order; видаляє гостя; офіціант бачить замовлення.
- **API** — авторизація (не Owner, гість, неавторизований), валідація (неіснуюча група, бронювання чи учасник; чужий учасник; повторний submit), а також підготовка даних для всіх сценаріїв.

Стан після дії (статус замовлення та групи, склад замовлення ресторану) перевіряється через API, а те, що бачить користувач, — на екранах.

### Тестові дані

- Кожен сценарій створює все потрібне сам: свіжого клієнта-Owner'а (`AQA-Sample-…`), **власний стіл** і **власні страви** з відомими цінами на локації офіціанта, бронювання для групи та гостей. Сценарії незалежні й ідуть паралельно.
- Окрема локація на сценарій не підходить: клієнт може бронювати лише там, де є офіціант. Тому стіл і страви створюються на локації стандартного офіціанта (`WAITER_*`).
- Час бронювання генерується динамічно: полудень UTC через 1–8 тижнів.
- Після сценарію бронювання скасовується (група закривається разом із сесіями гостей), а стіл і страви видаляються. Клієнтів `AQA-Sample-` без admin-токена видалити не можна; sweep показує їх, а з `ADMIN_*` видаляє.
- В UI Owner і офіціант входять без форми: токени й профіль кладуться в `localStorage` до завантаження сторінки. Тож паролі не потрапляють у trace. Сам вхід через форму перевіряє `Auth.feature`.

### Поведінка, не визначена в ДЗ (тестується саме так)

| Випадок | Поведінка |
|---|---|
| Група, до якої ніхто не приєднався | У списку лише Owner (він завжди перший учасник); сума = сума його pre-order, без pre-order — 0. Не `participants: []` |
| Загальна сума | API: сума всіх кошиків. Екран Owner'а: **Grand Total — лише підтверджені кошики**, тобто те, що піде в ресторан (вимога ДЗ фронту); непідтверджені позначені «Still choosing», показано нагадування |
| Список бачить не Owner | Інший клієнт або офіціант → `403`; гість (його токен не Cognito) або неавторизований → `401` |
| Неіснуюча група | `404`; на екрані «Group reservation not found» |
| Немає підтверджених кошиків / немає pre-order Owner'а / бронювання не відповідає групі | `400`, група лишається `Open`; на екрані — помилка, статус «Collecting pre-orders». Без підтвердженого кошика кнопка Submit неактивна |
| «У підтверджених кошиках немає страв» | Через продукт недосяжно: порожній кошик не підтверджується (`The cart is empty`), підтверджений кошик не змінюється (`already submitted`). Ці два правила й перевіряються |
| Повторний submit | `400`, `errorCode: GroupReservationSubmitted`; кількості не подвоюються |
| Submit, перегляд чи видалення не від Owner'а | `403` / `401`, як вище |
| Видалення гостя іншої групи | `400` |
| Видалення неіснуючого учасника | `404` |
| Видалення власної сесії Owner'а | `400` |
| Видалення після відправки pre-order | `400`, `GroupReservationSubmitted` |
| Видалення останнього гостя | Дозволено; лишається Owner |
| Список одразу після зміни кошика | Сесії гостей читаються через глобальний вторинний індекс DynamoDB, а він eventually consistent: зміна з'являється в списку Owner'а з невеликою затримкою. Кроки, що перевіряють список, перечитують його, як це зробив би Owner (Refresh або повторний запит), до 5 разів; якщо список так і не оновився, крок падає |
| Екран офіціанта | Відкривається напряму з тими даними навігації, які кладе картка бронювання (`sessionStorage.waiterPreOrderData`), щоб не залежати від фільтрів дати й часу на дашборді |

### Знайдені дефекти

- **Втрачене оновлення меню локації (бекенд, виправлено).** Створення, оновлення й видалення страви та редагування локації перезаписували весь запис локації. Паралельні зміни меню затирали одна одну: щойно створена страва «не існувала на локації». Тепер `specialtyDishIds` змінюється атомарними `UpdateItem` (`LocationRepository`).
- **`docs/ApiRoutes.md` розходиться з кодом:**
  - `/guest-sessions/{id}/dishes` замість `/guest/sessions/{id}/dishes`;
  - не описано обов'язковий `quantity` при видаленні страви з кошика;
  - інша форма відповіді `GET /guest/groups/{id}/status`.
- **Форма реєстрації** показує для прізвища тексти помилок імені. Це сценарії `@known-defect` з наданого фреймворку, вони навмисно червоні.

### Запуск

Налаштування `.env` описано нижче, у «Configuration». Для цих сценаріїв потрібні `UI_URL`, `API_URL` і `WAITER_*`. Бекенд призначає бронювання найменш зайнятому офіціанту локації, тому для сценаріїв з екраном офіціанта цей офіціант має бути на своїй локації єдиним.

```bash
dotnet build
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=L1]"     # рівень: L1 | L2 | L3
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=api]"    # стадія: api | ui
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=e2e]"    # наскрізний сценарій
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=smoke]"  # найважливіші
```

Звіти лежать у `TestResults/`:
- `reqnroll/reqnroll_report.html` — за сценаріями й кроками;
- `*-report.html` і `*.trx` — TUnit;
- `traces/*.zip` — Playwright trace впалих UI-сценаріїв; відкриваються `npx playwright show-trace <file>.zip`.

У CI (`.github/workflows/aqa-tests.yml`, Actions → AQA Tests → Run workflow) можна вибрати рівень, стадію або `e2e`. Звіти й traces публікуються як артефакти навіть тоді, коли тести впали.

## Run the tests

```bash
cp .env.example .env        # then fill in your own values (see "Configuration")
dotnet build
dotnet run --no-build -- --report-trx                          # everything
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=smoke]"   # by tag
```

Everything a run produces lands in `TestResults/`: the TUnit report and TRX, the Reqnroll report in `reqnroll/`, and Playwright traces in `traces/` (open one with `npx playwright show-trace <file>.zip`).

Some scenarios are expected to fail (`@known-defect`, bugs in the application) or are skipped (`@wip`, no admin account configured). That is intentional.

## Quality gates

| Where | What runs |
|---|---|
| Git hooks (local) | `pre-commit`: `dotnet format` check and Gherkin lint on staged files · `commit-msg`: Conventional Commits · `pre-push`: build with analyzers |
| `aqa-quality.yml` | on every push and pull request: no generated files tracked, format, build (warnings are errors), Gherkin lint, test discovery |
| `aqa-tests.yml` | the scenarios themselves, nightly and on demand (Actions → AQA Tests → Run workflow) |

Enable the hooks once per clone, from the repository root:

```bash
dotnet tool restore --tool-manifest aqa/restaurant-aqa-framework/.config/dotnet-tools.json
git config core.hooksPath aqa/restaurant-aqa-framework/.husky
```

The hooks only act on commits and pushes that touch `aqa/restaurant-aqa-framework`. Gherkin lint needs Node.js.

## Configuration

There is no shared environment and no committed `.env`: copy `.env.example` to `.env` and fill it in with the data of **your own** deployment (see "Testing your own deployment"). `.env` is git-ignored; never commit it.

Settings come from `.env` or real environment variables (variables win):

| Setting | Meaning |
|---|---|
| `UI_URL` | where the frontend is served |
| `API_URL` | base URL of the backend API, including the stage (for example `…/develop`) |
| `WAITER_USERNAME`, `WAITER_PASSWORD` | a waiter account that already exists in the environment under test |
| `TESTTABLE_ID`, `TESTTABLE_WAITER_*` | a table and the waiter assigned to it (only for scenarios that need them) |
| `ADMIN_USERNAME`, `ADMIN_PASSWORD` | an admin account; without it the admin scenarios are skipped |

The full list, with defaults, is in `.env.example`.

## Testing your own deployment

### If you deployed the backend / frontend / DevOps homework

1. `cp .env.example .env` and set `UI_URL` and `API_URL` to your deployment (API URL includes the stage, for example `…/develop`).
2. Waiters cannot be created by signing up, so create a waiter (and, for the admin scenarios, an admin) in *your* environment and put its credentials in `WAITER_*` / `ADMIN_*`. Optionally add a table and its assigned waiter (`TESTTABLE_*`).
3. Run `dotnet build` and a small slice first (`--treenode-filter "/*/*/*/*[Category=smoke]"`).

### If you have not deployed your own homework

If you deployed the original application, test that: set `UI_URL` / `API_URL` as above. Otherwise point the framework at a comparable public site (Booking, Airbnb, OpenTable, …) and write all the layers for it yourself: Gherkin features, step definitions, Screenplay tasks and questions, page objects with locators, and API clients where the site has a usable API. Use only flows you are allowed to automate (no purchases, no real bookings, respect the site's terms), and set `UI_URL` to that site.

1. **Generate a skeleton with Playwright codegen** (see "Presetup instructions"): record one flow, for example search → open a result → check details. Save the generated code to a scratch file, it is only raw material.
2. **Split it across the layers**: locators and small actions go to `Ui/` page objects, the flow becomes a task in `Screenplay/Tasks/`, the read-backs become questions in `Screenplay/Questions/`, the business wording goes to `Features/` and thin glue to `StepDefinitions/`.
3. **Replace weak locators.** Codegen often emits positional or text-fragile selectors. Prefer role + accessible name, label, test id, then CSS (see [CLAUDE.md](CLAUDE.md)); check them in the Playwright inspector.
4. Tag scenarios `@ui` / `@api`, keep them independent, and run `npx gherkin-lint -c .gherkin-lintrc "Features/*.feature"`.

### CI on your own public repository

Publish your work in your own public Git repository (the link is the deliverable). Workflows are disabled in a fresh fork until you enable them. Add the same values under Settings → Secrets and variables → Actions:

- **Variables:** `AQA_UI_URL`, `AQA_API_URL`, `AQA_TESTTABLE_ID`
- **Secrets:** `AQA_WAITER_USERNAME`, `AQA_WAITER_PASSWORD`, `AQA_ADMIN_USERNAME`, `AQA_ADMIN_PASSWORD`, `AQA_TESTTABLE_WAITER_USERNAME`, `AQA_TESTTABLE_WAITER_PASSWORD`

`aqa-quality.yml` runs on every push and pull request; `aqa-tests.yml` (nightly and on demand) tests whatever those variables point at. Never put credentials in the repository, in `.env.example` or in test code.

## Presetup instructions

This framework contains PlaywriteInstaller script so you don't need to setup it manually.
For full BDD highlighting in your IDE you may need to install Reqnroll extension https://docs.reqnroll.net/latest/ide-integrations/index.html
To start the Playwright code generator open Powershell in the framework directory and enter the line:
```bash
bin/Debug/net10.0/playwright.ps1 codegen "<your UI_URL or the public site you test>"
```
Build first so `playwright.ps1` exists. The URL is optional.
