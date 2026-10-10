# JiraReporter — گزارش‌ساز جیرا (.NET 8 + EF Core + SQLite)

سرویس .NET به REST API جیرا متصل می‌شود، ایشوها را در SQLite ذخیره می‌کند و گزارش‌های چرخه، وضعیت، WIP Aging، اسپرینت، بار کاری و نسبت باگ را ارائه می‌دهد.

## اجرای Jira محلی با Docker Compose

پیش‌نیاز: Docker Desktop (یا Docker Engine) و Docker Compose v2. فایل `docker-compose.yml` در ریشه پروژه، Jira Software و PostgreSQL 15 را اجرا می‌کند. داده‌های Jira و PostgreSQL در volumeهای دائمی `jira_data` و `postgres_data` نگه‌داری می‌شوند.

از پوشه‌ای که `docker-compose.yml` در آن است اجرا کنید:

```bash
docker compose up -d
# مشاهده لاگ‌های راه‌اندازی Jira
docker compose logs -f jira
```

راه‌اندازی اولیه چند دقیقه زمان می‌برد. سپس به `http://localhost:8080` بروید و مراحل راه‌اندازی وب Jira را کامل کنید. دیتابیس از قبل از طریق متغیرهای `ATL_JDBC_*` به PostgreSQL در شبکه Compose متصل است. اطلاعات دیتابیس موجود در Compose فقط برای توسعه محلی است؛ برای استفاده غیرمحلی رمز را عوض کنید و فایل حاوی رمز را منتشر نکنید.

توقف سرویس‌ها (بدون پاک کردن داده‌ها):

```bash
docker compose down
```

پاک کردن کامل داده‌های Jira و PostgreSQL (غیرقابل‌بازگشت):

```bash
docker compose down -v
```

### لایسنس ارزیابی رایگان

در راه‌انداز Jira، گزینه استفاده از لایسنس را انتخاب کنید و از بخش ارزیابی، راهنمای ارائه‌شده برای دریافت لایسنس Jira Software Data Center را دنبال کنید. همچنین می‌توانید وارد [my.atlassian.com](https://my.atlassian.com/) شوید، محصول Jira Software Data Center را برای ارزیابی انتخاب/ثبت کنید و لایسنس ارزیابی را بسازید یا کپی کنید. این لایسنس موقت و مشروط به شرایط فعلی Atlassian است؛ در صفحه مدیریت لایسنس Jira آن را وارد کنید. از لایسنس مجاز و نسخه/تعداد گره مناسب استفاده کنید؛ Compose بالا یک نمونه تک‌گرهی برای تست محلی است.

### پروژه و داده نمونه Scrum/Kanban

1. در Jira وارد شوید و از **Projects → Create project** یک پروژه **Scrum** بسازید (مثلاً نام `Sample Scrum`, کلید `SCRUM`)؛ قالب Scrum معمولاً برد Scrum را نیز می‌سازد.
2. از **Create** چند Issue نمونه در پروژه بسازید: یک Epic و چند Story/Task/Bug. برای هرکدام عنوان، توضیح، Assignee و Priority وارد کنید. برای نمونه گزارش‌های اسپرینت، به Storyها Story point بدهید، یک Sprint بسازید و ایشوها را به آن منتقل کنید؛ سپس Sprint را شروع کنید و وضعیت چند ایشو را تغییر دهید.
3. برای Kanban، از **Boards → View all boards → Create board → Create a Kanban board** استفاده کنید و گزینه ساخت برد برای پروژه موجود را انتخاب کنید. پروژه `SCRUM` را انتخاب کنید، یا در صورت نیاز یک پروژه Kanban جداگانه بسازید. چند ایشو را بین ستون‌های برد جابه‌جا کنید تا داده تغییر وضعیت داشته باشید.
4. برای استفاده از پروژه در JiraReporter، کلید پروژه باید در JQL قابل‌مشاهده باشد. در صورت استفاده از JQL محدودکننده، مقدار `Jira:Jql` را متناسب با کلید پروژه (مثلاً `project = SCRUM`) تنظیم کنید.

### تنظیم JiraReporter برای Jira محلی

مقدار پایه و کلید پروژه را در `JiraReporter.Api/appsettings.json` تنظیم کنید. اعتبارنامه را در فایل قرار ندهید؛ آن‌ها را در محیط اجرا با نام‌های `Jira__Email` و `Jira__ApiToken` تعریف کنید:

```json
"Jira": {
  "BaseUrl": "http://localhost:8080",
  "Email": "",
  "ApiToken": "",
  "ProjectKey": "JR",
  "SeedOnStartup": false,
  "Jql": "project = SCRUM"
}
```

این پروژه در وضعیت فعلی برای Jira Cloud نوشته شده است: `JiraClient` از مسیر `/rest/api/3/search` و Basic auth به شکل email/API token استفاده می‌کند. Jira Server/Data Center محلی ممکن است به‌جای آن REST API v2 و روش احراز هویت متفاوت (برای مثال PAT با Bearer) نیاز داشته باشد؛ بنابراین صرفاً تغییر `BaseUrl` برای همگام‌سازی تضمین‌شده کافی نیست و `JiraClient` باید با نسخه Jira و روش احراز هویت انتخابی هماهنگ شود. API token حساب Atlassian Cloud الزاماً برای Jira محلی قابل‌استفاده نیست.

## اجرای برنامه و ساخت داده‌ی نمونه در Jira

این مسیر برای **Jira Cloud** است. Jira Server/Data Center محلی با پیاده‌سازی فعلی کلاینت سازگار نیست؛ تغییر `BaseUrl` به‌تنهایی کافی نیست، چون کلاینت از REST API v3 و احراز هویت Cloud استفاده می‌کند.

### آماده‌سازی Jira

1. در Jira Cloud، یک پروژه‌ی **Scrum** بسازید و یک Scrum board برای آن داشته باشید. کلید پروژه را یادداشت کنید؛ در ادامه از `JR` به‌عنوان نمونه استفاده شده است.
2. مطمئن شوید پروژه نوع‌های Issue لازم (`Story`, `Task`, `Bug`) و workflow قابل‌انتقال بین وضعیت‌های `To Do`, `In Progress`, `In Review` و وضعیت‌های نهایی را دارد. اگر وضعیت یا انتقالی در workflow موجود نباشد، Seeder نمی‌تواند همان وضعیت را برای آن Issue تنظیم کند.
3. برای حساب Jira یک API token بسازید: `id.atlassian.com/manage-profile/security/api-tokens` → **Create API token**.

### تنظیم و اجرای Seeder

در `JiraReporter.Api/appsettings.json`، این مقادیر را با Jira خود هماهنگ کنید:

```json
"Jira": {
  "BaseUrl": "https://your-company.atlassian.net",
  "ProjectKey": "JR",
  "SeedOnStartup": true,
  "Jql": "project = JR"
}
```

`ProjectKey` و پروژه‌ی داخل `Jql` باید یکسان باشند. ایمیل و API token را در همان PowerShellای که برنامه را اجرا می‌کنید، به‌صورت متغیر محیطی وارد کنید:

```powershell
$env:Jira__Email = "your-account@example.com"
$env:Jira__ApiToken = "your-api-token"
cd JiraReporter.Api
dotnet restore
dotnet run
```

با `SeedOnStartup: true`، Seeder روی برد Scrum چهار Sprint و ۱۲۰ Issue بر اساس داده‌های Seeder دیتابیس می‌سازد. پس از اتمام ساخت، `SeedOnStartup` را دوباره `false` کنید. Issueها با برچسب `jira-reporter-seed` مشخص می‌شوند؛ اگر حتی بخشی از آن‌ها در پروژه وجود داشته باشد، اجرای بعدی برای جلوگیری از تکرار Seeder را رد می‌کند. برای تلاش مجدد پس از اجرای ناقص، Issueهای دارای این برچسب را در Jira پاک کنید.

Jira زمان‌های `Created`/`Updated` و changelog قدیمی را از طریق این API قابل backdate نمی‌کند. بنابراین خلاصه، نوع، Story point و داده‌ی Sprint بر اساس Seeder محلی ساخته می‌شوند، اما تاریخ‌ها و زمان‌بندی تاریخچه‌ی وضعیت واقعی و مربوط به زمان اجرا هستند. Assignee نیز فقط وقتی تنظیم می‌شود که کاربر قابل‌تخصیصی با همان نام در پروژه‌ی Jira وجود داشته باشد.

### همگام‌سازی داده‌ها به SQLite و مشاهده‌ی گزارش‌ها

Seeder مستقیم SQLite در کد موجود است، اما در حال حاضر از `Program.cs` اجرا نمی‌شود. برای ذخیره‌ی Issueهای واقعی ساخته‌شده در Jira در دیتابیس گزارش‌ساز:

1. مطمئن شوید `Jira:Jql` در `appsettings.json` همان پروژه را انتخاب می‌کند.
2. برنامه را اجرا کنید و Swagger را از آدرسی که در کنسول نمایش داده می‌شود باز کنید (معمولاً `http://localhost:5000/swagger`).
3. درخواست `POST /api/sync` را اجرا کنید. این درخواست Issueها و changelog فعلی Jira را به SQLite همگام می‌کند.
4. گزارش‌ها را از Swagger یا داشبورد اجرا کنید. همگام‌سازی خودکار پس‌زمینه فعلاً غیرفعال است؛ پس از هر تغییر در Jira، دوباره `POST /api/sync` را اجرا کنید.

## گزارش‌ها و APIها

در endpointهایی که این پارامترها را می‌پذیرند، `projectKey` گزارش را به یک پروژه و `sprintName` آن را به اسپرینت فعلی Issue محدود می‌کند. هر دو اختیاری‌اند. برای نمونه: `/api/reports/cycle-time?projectKey=JR&sprintName=Sprint%2001%20-%20Foundation`.

| Method | Route | خروجی و نحوه‌ی محاسبه |
|---|---|---|
| GET | `/api/reports/sprints?projectKey=` | فهرست نام‌های Sprint متمایز که در Issueهای ذخیره‌شده وجود دارند. |
| GET | `/api/reports/cycle-time?projectKey=&sprintName=` | برای هر Issue دارای `Resolved`، کلید، تاریخ حل و فاصله‌ی `Created` تا `Resolved` برحسب روز (یک رقم اعشار). در کد فعلی میانگین، میانه، صدک‌ها یا فیلتر تاریخ `from/to` برگردانده نمی‌شود. |
| GET | `/api/reports/time-in-status?projectKey=&sprintName=` | برای هر وضعیت فعلی، تعداد Issue و `TotalHours`. مقدار ساعت از زمان `Created` تا اکنون برای هر Issue (حداقل یک ساعت) جمع می‌شود و تاریخچه‌ی تغییر وضعیت در محاسبه به کار نمی‌رود؛ بنابراین این مقدار زمان واقعی سپری‌شده در هر وضعیت نیست. |
| GET | `/api/reports/wip-aging?projectKey=&sprintName=` | Issueهای حل‌نشده و غیرنهایی، همراه عنوان، وضعیت، مسئول و `ageDays`. سن از `Created` تا اکنون محاسبه می‌شود، نه از آخرین `Updated`؛ پارامتر `days` در پیاده‌سازی فعلی وجود ندارد. |
| GET | `/api/reports/sprint-scope?projectKey=&sprintName=` | برای هر Sprint تعداد `initialCommitment`، `completedScope` و `remainingScope`. مقدار اولیه در حال حاضر تعداد Issueهای فعلاً منتسب به Sprint است، نه snapshot تعهد ابتدای Sprint؛ تکمیل بر اساس وضعیت‌های نهایی تشخیص داده می‌شود. |
| GET | `/api/reports/sprint-velocity?projectKey=` | برای هر Sprint، Story point تعهدشده و تکمیل‌شده و `sayDoRatio` درصدی. Issue بدون Story point به‌عنوان ۱ امتیاز حساب می‌شود؛ عضویت فعلی Sprint مبناست. |
| GET | `/api/reports/rework-rate?projectKey=` | برای هر Sprint، تعداد Issueهای بازکاری‌شده، تعداد انتقال‌های بازکاری و درصد Issueهای بازکاری‌شده. بازکاری با انتقال از وضعیتی شامل Review/Test/QA/Done/Closed به وضعیتی شامل Progress/To Do/Reopen تشخیص داده می‌شود. |
| GET | `/api/reports/workload?projectKey=` | برای هر مسئول (یا `Unassigned`): تعداد کارهای باز، تعداد Issueهای حل‌شده در ۳۰ روز اخیر و مجموع Story point کارهای باز. Story point خالی در جمع این گزارش صفر در نظر گرفته می‌شود. |
| GET | `/api/reports/individual-performance?projectKey=&sprintName=` | خلاصه‌ی جداگانه برای هر مسئول در پروژه و اسپرینت انتخابی: کل موارد منتسب، موارد باز، موارد حل‌شده در ۳۰ روز اخیر، Story Point تکمیل‌شده در این بازه، میانگین زمان چرخه‌ی همان موارد و تعداد Issueهای دارای انتقال بازکاری. این محاسبه بر اساس مسئول فعلی Issue است، نه مسئول آن هنگام انجام کار. |
| GET | `/api/reports/bug-ratio?projectKey=&sprintName=` | تعداد کل Issueها، تعداد نوع `Bug` و درصد باگ از کل؛ بازه‌ی زمانی ندارد. |
| GET | `/api/reports/quarterly-distribution?projectKey=` | Issueهای حل‌شده را بر اساس فصل تقویمی تاریخ `Resolved` گروه‌بندی می‌کند و تعداد کل، Bug، Feature/Task و `unplanned` را می‌دهد. در تعریف فعلی، همه‌ی Bugها نیز unplanned شمرده می‌شوند؛ یا Summary دارای `[unplanned]` باشد. |
| POST | `/api/sync` | همگام‌سازی دستی Issueها و changelog از Jira به SQLite. برای دیدن تغییرات تازه‌ی Jira در گزارش‌ها، این درخواست را دوباره اجرا کنید. |
| GET | `/api/sync/logs` | حداکثر ۱۰۰ لاگ همگام‌سازی اخیر. |

**نکته درباره‌ی دقت شاخص‌ها:** وضعیت‌های `Done`, `Closed`, `Resolved`, `Canceled` و `Cancelled` در گزارش‌ها نهایی محسوب می‌شوند. `time-in-status` فعلی زمان هر وضعیت را از تاریخچه‌ی انتقال‌ها استخراج نمی‌کند و `wip-aging` هم عمر Issue را نشان می‌دهد، نه مدت سکون از آخرین به‌روزرسانی. همچنین `sprint-scope` تعهد اولیه را از snapshot تاریخی نمی‌خواند. این محدودیت‌ها در پیاده‌سازی فعلی گزارش‌ها هستند و هنگام تفسیر نتایج باید در نظر گرفته شوند.

گزارش فردی برای مرور روند و ظرفیت کاری است. چون مدل فعلی تاریخچه‌ی تغییر Assignee را ذخیره نمی‌کند، اگر مسئول یک Issue بعداً عوض شده باشد، آمار آن در همگام‌سازی بعدی به نام مسئول فعلی ثبت می‌شود؛ از این خروجی به‌تنهایی برای رتبه‌بندی افراد استفاده نکنید.

## Notes

- Authentication in the current client: Basic with `email:apiToken` (Cloud-oriented).
- `IssueStatusChange` is populated from changelog and refreshed for changed issues on sync.
- Default application database: `Data Source=jira.db` (migrated automatically).
