# FILE LIST & PAGE COUNTER
### استخراج أسماء الملفات وعدد الصفحات

أداة Windows Desktop تعمل **بدون إنترنت**، تقرأ ملفات الأرشيف من مكانها على القرص
**للقراءة فقط**، تحسب عدد صفحات كل ملف، ثم تنتج ملف **Microsoft Word (DOCX)** حقيقيًا
ومنسّقًا وجاهزًا للطباعة.

---

## 1. البنية المعمارية (Architecture)

ثلاث طبقات منفصلة، والمنطق كله في مكتبة مستقلة قابلة للاختبار:

```
FileListPageCounter.sln
│
├── src/FileListPageCounter.Core          (net8.0 — كل المنطق، بلا أي واجهة)
│   ├── Common/
│   │   ├── ReadOnlyFile.cs        ← الباب الوحيد لفتح أي ملف أصلي (FileAccess.Read فقط)
│   │   ├── NaturalComparer.cs     ← الترتيب الطبيعي 1, 2, 3, 10, 11, 20
│   │   └── Strings.cs             ← النصوص العربية المشتركة
│   ├── Models/                    ← FileEntry, ScanOptions, ReportOptions, ScanResult …
│   ├── Scanning/
│   │   ├── FileDiscovery.cs       ← تعداد المجلد/المجلدات الفرعية أو الملفات المحددة (Streaming)
│   │   ├── FileNameHelper.cs      ← حذف الامتداد الأخير فقط
│   │   ├── ScanService.cs         ← تشغيل الفحص بالتوازي + التقدم + التحقق من السلامة
│   │   └── EntryOrganizer.cs      ← التصفية والترتيب والترقيم (بدون قراءة القرص مجددًا)
│   ├── PageCounting/
│   │   ├── IPageCounter.cs        ← نقطة التوسعة لإضافة أنواع ملفات جديدة
│   │   ├── PageCounterRegistry.cs ← ربط الامتداد بالعدّاد المناسب
│   │   ├── PdfPageCounter.cs      ← PdfPig: قراءة شجرة الصفحات فقط
│   │   ├── RawPdfPageScanner.cs   ← خطة احتياطية لملفات PDF التالفة
│   │   ├── ImagePageCounter.cs    ← كل صورة = صفحة واحدة
│   │   └── TiffFrameCounter.cs    ← قراءة سلسلة IFD في TIFF متعدد الصفحات
│   ├── Reporting/WordReportBuilder.cs  ← Open XML SDK: إنشاء DOCX حقيقي
│   ├── Integrity/IntegrityVerifier.cs  ← إثبات أن الملفات الأصلية لم تتغيّر
│   └── Diagnostics/ProcessingLog.cs    ← سجل الأخطاء (خارج مجلد المصدر دائمًا)
│
├── src/FileListPageCounter.App           (net8.0-windows, WPF — الواجهة فقط)
│   ├── MainWindow.xaml / .cs
│   ├── ViewModels/MainViewModel.cs
│   └── Infrastructure/  (RelayCommand, AppSettings, WindowsDialogService …)
│
└── tests/FileListPageCounter.Tests       (net8.0, xUnit — 40+ اختبارًا)
```

**قاعدة معمارية واحدة تحكم المشروع:** أي ملف أصلي لا يُفتح إلا عبر `ReadOnlyFile`،
وهو يفتح دائمًا بـ `FileMode.Open` و `FileAccess.Read`. لا يوجد في المشروع كله مسار
كود واحد يفتح ملفًا أصليًا للكتابة. الملف الوحيد الذي يُكتب هو تقرير Word الذي يختار
المستخدم مكانه بنفسه.

**التقنية المختارة:** C# / .NET 8 (LTS) / WPF — الأكثر استقرارًا لإنتاج EXE مستقل
لسطح مكتب Windows. المكتبات: `DocumentFormat.OpenXml` (Open XML SDK) لملف DOCX،
و`PdfPig` (مُدارة بالكامل، مفتوحة المصدر) لقراءة عدد صفحات PDF.

---

## 2. الشرط الأساسي: READ-ONLY

| الممنوع | كيف يُمنع تقنيًا |
|---|---|
| تعديل أو الكتابة داخل ملف أصلي | كل فتح يمرّ عبر `ReadOnlyFile` بصلاحية `FileAccess.Read` — نظام التشغيل نفسه يرفض أي كتابة |
| إعادة التسمية / النقل / الحذف / النسخ | لا يوجد استدعاء واحد لـ `File.Move` أو `File.Copy` أو `File.Delete` أو `Directory.Move` في كامل الكود |
| تغيير التواريخ أو الخصائص أو الصلاحيات | لا يوجد استدعاء لـ `File.SetLastWriteTime` أو `SetCreationTime` أو `SetAttributes` |
| إنشاء ملفات مؤقتة/Cache/Backup في مجلد المصدر | البرنامج **لا ينشئ ملفات مؤقتة أصلًا**؛ السجل والإعدادات تُحفظ في `%LOCALAPPDATA%` و`%APPDATA%` |
| الرفع إلى الإنترنت / Cloud / API | المكتبة الأساسية **لا تُشير إلى أي تجميعة شبكة** (`System.Net.*`)، ويوجد اختبار يفشل فور كسر هذا الشرط |

بالإضافة إلى ذلك، خيار **«التحقق من عدم تغيّر الملفات الأصلية بعد الفحص»** (مفعّل افتراضيًا)
يلتقط قبل الفحص بصمة لكل ملف (الاسم + الحجم + تاريخ التعديل + تاريخ الإنشاء + Attributes)
ويقارنها بعد الانتهاء، ويعرض تحذيرًا فوريًا عند أي اختلاف.

PDF يُفتح في وضع القراءة فقط، ولا تُقرأ إلا شجرة الصفحات (Metadata) — لا يُفكّ ترميز أي
محتوى أو خط أو صورة — ثم يُغلق دون أي تغيير.

---

## 3. أنواع الملفات المدعومة

| النوع | طريقة الحساب |
|---|---|
| `.pdf` | عدد الصفحات الفعلي من شجرة صفحات المستند |
| `.jpg` `.jpeg` `.jpe` `.jfif` `.png` `.bmp` `.gif` `.webp` | صفحة واحدة لكل ملف |
| `.tif` `.tiff` | عدد الإطارات (الصفحات) الحقيقي، أو صفحة واحدة عند إيقاف الخيار |

> **ملاحظة عن TIFF:** المواصفات تنصّ على أن كل صورة تُحسب صفحة واحدة، وهذا هو السلوك
> عند إلغاء تفعيل الخيار. لكن TIFF هو الصيغة الوحيدة في القائمة التي قد تحتوي فعليًا على
> عدة صفحات داخل ملف واحد (وهو أمر شائع في الأرشيف الممسوح ضوئيًا)، لذلك يأتي خيار
> **«احتساب صفحات ملفات TIFF متعددة الصفحات»** مفعّلًا افتراضيًا. يمكن إلغاؤه من الواجهة
> للعودة إلى قاعدة «صورة = صفحة واحدة» حرفيًا.

أي امتداد آخر: يظهر **غير معروف** في عدد الصفحات (أو يُخفى إذا فُعّل «تجاهل الملفات غير المدعومة»).
الملفات التالفة لا توقف البرنامج إطلاقًا: تظهر بـ **غير معروف** ويُسجَّل سبب الخطأ في السجل.

**إضافة نوع جديد:** نفّذ `IPageCounter` وسجّله في `PageCounterRegistry.CreateDefault()`.
لا يتغيّر أي جزء آخر من البرنامج.

---

## 4. مخرجات Word

- **DOCX حقيقي** بصيغة Open XML (وليس HTML بامتداد مُغيَّر) — يوجد اختبار يفتح الحزمة
  ويتحقق من وجود `[Content_Types].xml` و`word/document.xml`.
- **A4 Portrait** (11906 × 16838 twips) مع هوامش 2 سم من كل جهة.
- **الخط: Arial**، الحجم الافتراضي **20** وقابل للتغيير (16 / 18 / 20 / 22 / 24).
- **اتجاه النص RTL** على مستوى المقطع والفقرات والفقرات داخل الجدول والجدول نفسه
  (`bidiVisual` — فيظهر عمود «م» على اليمين).
- **العنوان** «قائمة الملفات وعدد الصفحات» — Bold، في المنتصف، ثم إجمالي عدد الملفات
  وإجمالي عدد الصفحات.
- **الجدول:** `م | اسم الملف | عدد الصفحات` بحدود واضحة ورأس Bold مظلّل.
- **تكرار رأس الجدول** تلقائيًا في أعلى كل صفحة (`tblHeader`).
- **منع تقطيع الصفوف** بين صفحتين (`cantSplit` على كل صف).
- **ملخص في النهاية:** إجمالي عدد الملفات / إجمالي عدد الصفحات / عدد الملفات التي تعذر
  تحديد صفحاتها.
- **تذييل** برقم الصفحة لتسهيل الطباعة.

---

## 5. البناء (Build)

### المتطلب الوحيد
[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) على جهاز Windows.

### الطريقة الأسهل
انقر نقرًا مزدوجًا على **`build.cmd`**.

يقوم بـ: استرجاع الحزم ← تشغيل كل الاختبارات ← إنتاج EXE مستقل في مجلد `publish\`.

```
publish\FileListPageCounter.exe     ← انقر عليه مرتين وسيعمل البرنامج
```

الملف **Self-contained** و**Single-file**: لا يحتاج الجهاز المستهدف إلى تثبيت .NET
ولا إلى أي أداة أخرى. انسخ مجلد `publish` إلى أي جهاز Windows 10/11 (x64) وشغّله.
وجود `portable.txt` بجانب الـ EXE يجعل الإعدادات تُحفظ بجواره بدل `%APPDATA%` (وضع Portable).

### نسخة أخف الحجم
`build-framework-dependent.cmd` ينتج EXE صغيرًا (بضعة ميغابايت) لكنه يتطلب
تثبيت **.NET 8 Desktop Runtime** على الجهاز المستهدف.

### من سطر الأوامر مباشرة
```bat
dotnet publish src\FileListPageCounter.App -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

> البناء يتطلب Windows لأن المشروع يستخدم WPF. مكتبة `Core` ومشروع الاختبارات
> يبنيان ويعملان على أي نظام تشغيل.

---

## 6. الاختبارات (Testing)

```bat
run-tests.cmd
```
أو
```bat
dotnet test FileListPageCounter.sln -c Release
```

الاختبارات تُنشئ ملفات PDF و TIFF **حقيقية** (بجداول xref وسلاسل IFD صحيحة) داخل مجلد
مؤقت في `%TEMP%`، وليست محاكاة (Mocks).

| # | المطلوب | الاختبار |
|---|---|---|
| 1 | اختيار مجلد | `ScanServiceTests.A_single_file_is_processed`, `Subfolders_are_included_only_when_the_option_is_on` |
| 2 | اختيار ملفات متعددة | `ScanServiceTests.Multiple_selected_files_from_different_folders_are_processed` |
| 3 | قراءة ملف واحد | `ScanServiceTests.A_single_file_is_processed` |
| 4 | قراءة 100 ملف | `ScanServiceTests.A_hundred_files_are_all_processed_with_the_right_totals` |
| 5 | عدد كبير من الملفات | `ScanServiceTests.A_large_batch_...` (1000 ملف), `WordReportTests.A_large_report_...` |
| 6 | حذف الامتداد من الاسم | `FileNameTests` (8 حالات: عربي، إنجليزي، أرقام، أقواس، نقاط متعددة) |
| 7 | حساب صفحات PDF | `PageCountingTests.Pdf_page_count_is_exact` (1 / 2 / 7 / 64 صفحة) |
| 8 | الصور كصفحة واحدة | `PageCountingTests.Images_count_as_one_page`, `Single_frame_tiff_...` |
| 9 | ملف تالف | `PageCountingTests.A_corrupt_pdf_...`, `ScanServiceTests.A_damaged_file_does_not_stop_the_others` |
| 10 | ملف غير مدعوم | `PageCountingTests.An_unsupported_extension_...`, `ScanServiceTests.Unsupported_files_are_hidden_or_listed_...` |
| 11 | إنشاء DOCX حقيقي | `WordReportTests.The_output_is_a_real_open_xml_package_not_renamed_html` |
| 12 | RTL | `WordReportTests.The_document_is_right_to_left` |
| 13 | Font Size 20 | `WordReportTests.The_default_font_is_Arial_at_size_twenty` (+ اختبار لكل الأحجام) |
| 14 | A4 | `WordReportTests.The_page_is_A4_portrait_with_margins` |
| 15 | تكرار Header | `WordReportTests.The_header_row_repeats_on_every_page_and_rows_never_split` |
| 16 | Summary | `WordReportTests.A_summary_closes_the_document` |
| 17 | Natural Sort | `NaturalSortTests` (5 اختبارات), `ScanServiceTests.Rows_are_numbered_naturally_...` |
| 18 | عدم تعديل الملفات الأصلية | `ReadOnlyGuaranteeTests.Scanning_leaves_every_source_file_byte_for_byte_identical` (مقارنة بايت ببايت) |
| 19 | عدم تغيير Last Write Time | `ReadOnlyGuaranteeTests.Last_write_time_and_creation_time_are_untouched` |
| 20 | عدم إنشاء ملفات داخل مجلد المصدر | `ReadOnlyGuaranteeTests.No_file_is_created_renamed_or_deleted_...`, `OfflineAndPrivacyTests.A_full_run_writes_nothing_next_to_the_source_files` |
| 21 | عدم استخدام الإنترنت | `OfflineAndPrivacyTests.The_core_library_does_not_reference_any_networking_assembly` |
| 22 | عدم رفع أي ملف | نفس الاختبار السابق + `No_public_core_api_accepts_or_returns_a_uri` |

اختبارات إضافية للأمان: فتح الملفات بمشاركة `FileShare.Read` فقط (أي محاولة فتح للكتابة
كانت ستفشل)، وقراءة ملف مفتوح من برنامج آخر، وقراءة مجلد للقراءة فقط، واختبار يتأكد
من أن أداة التحقق نفسها تكتشف التغيير فعلًا.

---

## 7. الإعدادات (Configuration)

تُحفظ تلقائيًا وتُستعاد عند التشغيل التالي:

| الإعداد | الافتراضي |
|---|---|
| تضمين المجلدات الفرعية | مفعّل |
| تجاهل الملفات غير المدعومة | مفعّل |
| احتساب صفحات TIFF متعددة الصفحات | مفعّل |
| التحقق من سلامة الملفات الأصلية | مفعّل |
| عرض خيار فتح الملف بعد إنشائه | مفعّل |
| حجم الخط | 20 |
| الترتيب | حسب اسم الملف (Natural Sort) |

**مكان الحفظ:** `%APPDATA%\FileListPageCounter\settings.json`
— أو بجانب الـ EXE في وضع Portable. **لا يُكتب أي شيء داخل مجلد المصدر.**

**سجل التفاصيل:** يظهر زر «حفظ سجل التفاصيل» فقط عند وجود ملاحظات أو أخطاء، ويحفظ
الملف في `%LOCALAPPDATA%\FileListPageCounter\logs\`.

---

## 8. طريقة الاستخدام

```
تشغيل البرنامج (دبل كليك على EXE)
        ↓
[📁 اختيار مجلد]  أو  [📄 اختيار ملفات]
        ↓
قراءة الملفات مباشرة من القرص (شريط تقدم — الواجهة لا تتجمد، ويمكن الإيقاف)
        ↓
معاينة الجدول داخل البرنامج: م | اسم الملف | عدد الصفحات + الإجماليات
        ↓
[📝 إنشاء ملف Word]  →  اختيار مكان الحفظ
        ↓
رسالة نجاح + [فتح الملف]
```

الأداء: تُقرأ الملفات بالتوازي (عدد المعالجات، بحد أقصى 8 خيوط) دون قراءة محتوى الملفات
كاملة، ولا يوجد أي حد أعلى مصطنع لعدد الملفات — الحد الوحيد هو إمكانات الجهاز.
