# Customer Support CRM

نظام دعم العملاء — ASP.NET Core 10 + SQL Server backend, Vue 3 frontend, Arabic-first with
full RTL. Requirements come from `docs/requirements.md` (transcribed from
`azm_squad_customer_support_crm.pdf`), and the work is planned through
[squad-kit](https://github.com/AzmSquad/squad-kit) under `.squad/`.

---

## 1. المتطلبات

| | |
|---|---|
| .NET SDK | 10.0+ (`dotnet --list-sdks`) |
| Node.js | 20+ (`node -v`) |
| SQL Server | Express محلي على `.\SQLEXPRESS` |
| `dotnet-ef` | `dotnet tool install --global dotnet-ef` |

---

## 2. التشغيل

### أول مرة: اضبط ملف التطوير

الملف ده مستبعد من git لأنه بيحمل مفتاح توقيع الـ JWT وباسورد الأدمن:

```bash
cp backend/src/CustomerSupportCRM.Api/appsettings.Development.example.json backend/src/CustomerSupportCRM.Api/appsettings.Development.json
```

بعدها افتحه وغيّر القيمتين المعلَّمتين بـ `REPLACE-ME`:

- **`Jwt:SigningKey`** — 32 حرف على الأقل. التطبيق **بيرفض يشتغل** لو أقصر من كده أو فاضي
- **`Seed:AdminPassword`** — باسورد الأدمن اللي هيتعمل أول تشغيل

### الباك إند + Swagger

```bash
dotnet run --project backend/src/CustomerSupportCRM.Api --launch-profile http
```

- بيفتح **Swagger** تلقائياً على <http://localhost:5178/swagger>
- الـ API نفسه على <http://localhost:5178>
- `GET /health` للتأكد إن الخدمة شغالة

### الفرونت إند

في terminal تانية:

```bash
npm install --prefix frontend
```

```bash
npm run dev --prefix frontend
```

التطبيق على <http://localhost:5173>. الـ Vite proxy بيحوّل `/api` للباك إند، فالمتصفح
يفضل same-origin ومحتاجش CORS.

### بيانات الدخول

الإيميل والباسورد اللي حطيتهم في `Seed:AdminEmail` و `Seed:AdminPassword` في
`appsettings.Development.json` وقت الإعداد فوق.

---

## 3. الميجريشن — بيتنفّذ تلقائياً وقت التشغيل

في بيئة **Development** كل ميجريشن جديد يُطبَّق أول ما تشغّل المشروع، ومعاه seeding
البيانات المرجعية. اللوج بيقول لك بالاسم إيه اللي اتنفّذ:

```
[17:33:51 WRN] Applying 1 pending migration(s): 20260825141752_SystemConfiguration
[17:33:52 WRN] Applied 1 migration(s) successfully.
```

ولو مافيش جديد:

```
[17:34:02 INF] Database is up to date; no migrations to apply.
```

المفاتيح في `appsettings.Development.json`:

```jsonc
"Database": {
  "MigrateOnStartup": true,   // يطبّق أي ميجريشن ناقص
  "SeedOnStartup": true       // أدوار + أدمن + أقسام + فروع + تصنيفات + إعدادات
}
```

**في `appsettings.json` الاتنين `false` عن قصد.** التطبيق التلقائي على قاعدة مشتركة أو
إنتاج بيتسابق بين النسخ ومايدّي فرصة لمراجعة الـ deploy، فالبيئات دي تطبّق الميجريشن
كخطوة مقصودة.

### إضافة ميجريشن جديد

```bash
dotnet ef migrations add <MigrationName> --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api --output-dir Persistence/Migrations
```

وبعدها شغّل المشروع عادي — هيتطبّق لوحده.

### تطبيق يدوي (للإنتاج)

```bash
dotnet ef database update --project backend/src/CustomerSupportCRM.Infrastructure --startup-project backend/src/CustomerSupportCRM.Api
```

---

## 4. إزاي تختبر اللي اتعمل

كل الـ endpoints موجودة في Swagger. للمحمي منها لازم توكن:

1. في Swagger افتح **`POST /api/auth/login`** → Try it out → ابعت:
   ```json
   { "email": "<Seed:AdminEmail>", "password": "<Seed:AdminPassword>" }
   ```
2. انسخ `accessToken` من الرد.
3. اضغط زرار **Authorize** فوق على اليمين، والصق التوكن.
4. دلوقتي كل الـ endpoints شغالة.

> التوكن بيخلص بعد **15 دقيقة** عن قصد — الصلاحيات محفوظة جوّاه، فسحب صلاحية أو تعطيل
> حساب مايسريش قبل انتهاء التوكن، والـ 15 دقيقة هي الحد الأعلى للتأخير. لو خلص، اعمل
> login تاني أو استخدم `POST /api/auth/refresh`.

### اللي يستحق تجربته

**إدارة المستخدمين والحمايات** — `/api/users`
- `POST /api/users` — أنشئ موظف دعم بدور `Agent` وقسم
- `POST /api/users/{yourOwnId}/deactivate` → **409** (مش ممكن تعطّل نفسك)
- `PUT /api/users/{adminId}/roles` بـ `["Manager"]` → **409** (آخر أدمن)
- جرّب دور `["Customer","Agent"]` → **400** (ممنوع الجمع)

**عزل الأقسام** — الجزء الأمني الأهم
1. أنشئ موظفين، واحد في `SUP` وواحد في `BIL`
2. سجّل دخول بكل واحد وخُد توكنه
3. أنشئ عميل + تذكرة في كل قسم (بحساب الأدمن، لأنه يشوف الكل)
4. موظف `SUP` يعمل `GET /api/tickets` → يشوف تذكرته بس
5. موظف `SUP` يعمل `GET /api/tickets/{تذكرة BIL}` → **403**

**الصلاحيات** — بتوكن `Agent`:

| Endpoint | المتوقع |
|---|---|
| `/api/tickets`, `/api/customers`, `/api/dashboard/agent` | 200 |
| `/api/users`, `/api/audit-logs`, `/api/system-config/**` | **403** |
| `DELETE /api/customers/{id}`, `PUT /api/branding` | **403** |

**سجل التدقيق** — `/api/audit-logs`
- أي تعديل بيتسجّل تلقائياً مع JSON diff
- عدّل عميل، وبعدها `GET /api/audit-logs?entityName=Customer`
- GET فقط — مافيش أي verb كتابة عن قصد

**أسرار القنوات (write-only)**
1. `PUT /api/system-config/channels/1` بـ `apiKey`
2. `GET /api/system-config/channels` → `hasCredentials: true` بس **المفتاح نفسه مايرجعش**
3. `PUT` تاني **بدون** `apiKey` → القيمة المحفوظة تفضل موجودة
4. `GET /api/audit-logs?entityName=ChannelToggle` → السر مش موجود في السجل

**الهوية البصرية** — `/api/branding`
- `GET` بدون توكن (شاشة الدخول محتاجاها قبل التوكن)
- `PUT` باللون `#7c3aed` → حدّث المتصفح، اللون بيتغير فوراً
- جرّب `"primaryColor": "not-a-colour"` → **400**
- جرّب `"logoUrl": "javascript:alert(1)"` → **400**

### من واجهة التطبيق

بعد الدخول كأدمن، الشريط الجانبي فيه 6 بنود:
لوحة المتابعة · التذاكر · العملاء · المستخدمون والصلاحيات · سجل التدقيق · إعدادات النظام

الثلاثة الأخيرة تظهر بالصلاحية بس — لو دخلت بـ `Agent` مش هتلاقيهم، ولو كتبت الرابط
بإيدك الـ router هيرجّعك (والـ API كمان هيرفض).

جرّب كمان: زرار **EN/ع** فوق يقلب اللغة والاتجاه كامل، وزرار القمر/الشمس للثيم.

---

## 5. أوامر التحقق

```bash
dotnet test backend/CustomerSupportCRM.slnx
```

```bash
npm run build --prefix frontend
```

`npm run build` بيشغّل حارس الترجمة قبل البناء — لو مفتاح موجود في لغة وناقص في التانية،
أو `{placeholder}` مختلف، البناء يفشل.

---

## 6. هيكل المشروع

```
backend/
  CustomerSupportCRM.slnx                     (.NET 10 — .slnx مش .sln)
  src/CustomerSupportCRM.Domain/              الكيانات وقواعد سير التذكرة، بدون أي اعتماديات
  src/CustomerSupportCRM.Application/         DTOs، الخدمات، الـ validators، الواجهات
  src/CustomerSupportCRM.Infrastructure/      EF Core، Identity، JWT، التخزين، الـ seeder
  src/CustomerSupportCRM.Api/                 الـ controllers، الـ middleware، DI، Swagger
  tests/CustomerSupportCRM.Application.Tests/ 117 اختبار
frontend/                                     Vue 3 + Vite + TS + PrimeVue 5 + Tailwind 4
docs/requirements.md                          نص متطلبات الـ PDF
.squad/                                       قصص وخطط squad-kit
```

---

## 7. حالة العمل

**منفَّذ:** إدارة المستخدمين · نموذج الصلاحيات (21 صلاحية) · سجل التدقيق · إعدادات النظام
(ساعات العمل، العطل، الهوية البصرية، مفاتيح المزايا، القنوات) · تغيير كلمة المرور ·
عزل الأقسام · العملاء والتذاكر (الأساس) · لوحة متابعة الموظف · حارس الترجمة

**12 قصة** في `.squad/stories/`، منها **خطتان** منفَّذتان (`security-admin`, `platform`).
باقي 10 مناطق محتاجة خطة قبل التنفيذ:

```bash
squad new-plan .squad/stories/<feature>/<story>/intake.md --api -y
```

```bash
squad list
```

التفاصيل الكاملة لكل خطة — اللي نزل، الانحرافات وأسبابها، واللي فاضل — في
`.squad/plans/<feature>/00-overview.md`.
