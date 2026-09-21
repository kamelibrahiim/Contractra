# Contractra — مصفوفة تتبع المتطلبات

# Requirements Traceability Matrix

---

## 0. إدارة الوثيقة

| البند | القيمة |
|---|---|
| **المعرّف** | `requirements-traceability-matrix-ar` |
| **الإصدار** | **2.0.1** |
| **الحالة** | 🟢 مكتملة — **Normative** |
| **التاريخ** | 2026-09-17 |
| **الغرض** | ⭐ **إثبات أن كل متطلب مرتبط بقاعدة عمل وحالة استخدام وكيانات وصلاحية وحدث تدقيق ومعيار قبول واختبار ومصدر** — ⛔ **فلا متطلب يتيم ولا قاعدة معلَّقة في الهواء** |
| **يُقرأ مع** | `system-specification-and-team-reference-ar.md` (الأب) · `logical-data-model-ar.md` |

### 0.1 الأعمدة

| العمود | المعنى |
|---|---|
| **FR** | معرّف المتطلب |
| **BR** | قاعدة العمل الحاكمة |
| **UC** | حالة الاستخدام |
| **Entities** | كيانات البيانات المتأثرة |
| **Permission** | الصلاحية المطلوبة |
| **Audit Event** | حدث التدقيق |
| **AC** | معيار القبول |
| **TEST** | حالة الاختبار |
| **Source** | المستند المصدر |
| **Status** | 🟢 Confirmed · 🔵 Proposed · 🟡 Open |

---

## 1. ⭐ المتطلبات الحرجة — المصفوفة الكاملة

> **هذه المتطلبات ⭐ حرجة: فشل أيٍّ منها يمنع الإطلاق.**

| FR | BR | UC | Entities | Permission | Audit Event | AC | TEST | Source | Status |
|---|---|---|---|---|---|---|---|---|---|
| **FR-AUTH-006** استقلال `Payroll.View` | **BR-235** | UC-002 | `user_permissions` | `Permissions.Grant` | `PayrollViewGranted` · `PayrollViewRevoked` | **AC-047** | TEST-006 | ملحق التقرير §11.3 | 🟢 |
| **FR-AUTH-008** التحقق في الخادم | **BR-018** · BR-138 | كل UC | — | كل الصلاحيات | `AccessDenied` | **AC-020** · AC-056 | TEST-008 | ح-18 · ح-138 | 🟢 |
| **FR-004** تطبيع بلا تفرّد | **BR-100** · BR-168 | UC-003 | `parties` · `party_aliases` | `Parties.Manage` | `PartyCreated` | AC-063 | TEST-015 | ح-100 · ح-168 | 🟢 |
| **FR-005** كشف تشابه ينبّه ولا يدمج | **BR-106** · BR-100 | UC-003 | `party_similarity_candidates` | `Parties.Merge.Review` | `SimilarityCandidateResolved` | AC-064 | TEST-016 | ح-106 | 🟢 |
| **FR-011** منع تحويل الحساب التجميعي | **BR-031** | UC-003 | `aggregate_cost_accounts` | `Parties.Manage` | `CostResourceCreated` | AC-070 | TEST-022 | ح-31 | 🟢 |
| **FR-015** أمر إسناد إلزامي | **BR-197** · BR-196 | **UC-004** | `operations` · `assignment_orders` | `Operations.Manage` | `OperationActivated` | **AC-022** · AC-074 | TEST-026 · TEST-027 | ملحق العمليات §2 | 🟢 |
| **FR-019** تكليف مؤرَّخ | **BR-017** · BR-131 · BR-202 | **UC-006** | `engineer_site_assignments` | `Assignments.Manage` | `EngineerSiteAssignmentCreated` | AC-075 · AC-076 · AC-081 | TEST-028 … 030 | ملحق العمليات §3 | 🟢 |
| **FR-021** ثبات الحركات القديمة | **BR-202** · BR-020 | **UC-007** | `engineer_site_assignments` · `expenses` | `Assignments.Manage` | `EngineerSiteAssignmentClosed` | **AC-080** | TEST-035 | ح-202 | 🟢 |
| **FR-023 · FR-024** رؤية المهندس | **BR-201** · BR-016 | UC-009 | `engineer_site_assignments` · `sites` · `operations` | `Expenses.Create` | — | ⭐ **AC-018** | TEST-037 · TEST-038 | ح-201 | 🟢 |
| **FR-026** قيد الكمية للبند غير المتكرر | **BR-076** · BR-115 | UC-022 | `boq_items` · `claim_line_progress` | `Claims.Manage` | `ClaimCreated` | AC-083 | TEST-040 | ح-76 · العقد ص8 | 🟢 |
| **FR-034** منع تغيير أمر الإسناد بعد الاعتماد | **BR-200** | UC-004 | `operations` · `assignment_orders` | `Operations.Manage` | `AssignmentOrderRelinked` | AC-091 | TEST-048 | ملحق العمليات §2 ق11 | 🟢 |
| **FR-CUS-001** تسليم عهدة | **BR-025** · **BR-140** | **UC-008** | `custody_ledger_entries` · 🟢 `treasury_movements` · `financial_events` | `Custody.Disburse` | `CustodyDisbursed` | AC-092 · AC-093 | TEST-049 · 050 | ح-140 | 🟢 |
| **FR-CUS-002** منع المهندس من إضافة عهدة | **BR-025** | UC-008 | `custody_ledger_entries` | — | `AccessDenied` | AC-094 | TEST-051 | ح-25 | 🟢 |
| **FR-CUS-006** تصحيح حركة عهدة | **BR-143** | UC-015 | `custody_ledger_entries` | `Custody.Correct` | `CustodyAdjusted` | **AC-005** | TEST-055 | ح-143 | 🟢 |
| **FR-CUS-007** الرصيد مشتق | **BR-028** · BR-026 | **UC-016** | `custody_ledger_entries` | `Custody.ViewBalance` | — | AC-098 | TEST-056 | ح-28 | 🟢 |
| **FR-CUS-008** الرصيد بنص واضح | **BR-027** | UC-016 | — | `Custody.ViewBalance` | — | **AC-099** | TEST-057 | ح-27 | 🟢 |
| **FR-EXP-001** تسجيل مصروف | **BR-203** … **BR-208** | **UC-009** | `expenses` · `expense_attachments` | `Expenses.Create` | `ExpenseDraftCreated` | **AC-016** … **AC-019** · **AC-024** … **AC-026** | TEST-060 … 067 | ملحق العمليات §4–6 | 🟢 |
| **FR-EXP-002** أمر الإسناد تلقائي | **BR-199** | UC-009 | `expenses` · `operations` | `Expenses.Create` | — | ⭐ **AC-019** | TEST-073 | ح-199 | 🟢 |
| **FR-EXP-005** اختيارية المرفق المطلقة | **BR-207** | UC-009 · UC-014 | `expense_attachments` | `Expenses.Create` | `DocumentUploaded` | ⭐ **AC-023** · **AC-026** | TEST-076 · 077 | ملحق العمليات §8 | 🟢 |
| **FR-EXP-007** اعتماد المصروف | **BR-213** · **BR-186** | **UC-014** | `expenses` · `custody_ledger_entries` · `journal_entries` · `audit_events` | `Expenses.Approve` | ⭐ `ExpenseApproved` | **AC-001** · **AC-002** · **AC-027** | TEST-068 … 072 | ح-213 · ح-186 | 🟢 |
| **FR-EXP-011** تصحيح مصروف معتمد | **BR-213** · **BR-178** · BR-187 | **UC-015** | `expense_corrections` · `audit_event_links` | `Expenses.Correct` | `CorrectionPosted` | **AC-005** · **AC-029** | TEST-082 · 083 | ح-178 | 🟢 |
| **FR-EXP-015** تحققات الخادم العشرة | **BR-210** · BR-138 | UC-011 · UC-014 | `expenses` · `engineer_site_assignments` | `Expenses.Submit` | `AccessDenied` | **AC-020** · **AC-021** | TEST-087 … 096 | ملحق العمليات §11 | 🟢 |
| **FR-EXP-018** لا أثر قبل الاعتماد | **BR-219** · BR-209 | UC-016 | `expenses` · `custody_ledger_entries` | — | — | ⭐ **AC-038** | TEST-099 | ح-219 | 🟢 |
| **FR-EXP-019** منع الأثر المزدوج | **BR-103** · BR-213 | UC-014 | `custody_ledger_entries` | `Expenses.Approve` | `ExpenseApproved` | ⭐ **AC-027** | TEST-100 | ح-103 | 🟢 |
| **FR-WRK-001** يومية بلا أثر مالي | **BR-046** · BR-042 | **UC-017** | `attendance_entries` | `Attendance.Create` | `AttendanceRecorded` | AC-111 · AC-112 | TEST-104 · 105 | ح-46 | 🟢 |
| **FR-WRK-004** لا صيغة حرة | **BR-167** | UC-017 · UC-020 | `pay_rate_versions` · `payroll_lines` | `Payroll.Prepare` | `PayRateVersionCreated` | AC-115 | TEST-108 | ح-167 | 🟢 |
| **FR-WRK-009** اعتماد بلا نقد | **BR-047** · BR-223 | **UC-020** | `payroll_periods` · `journal_entries` | `Payroll.Approve` | `PayrollApproved` | AC-120 | TEST-113 | ح-47 | 🟢 |
| **FR-WRK-010** تكلفة بأساس الاستحقاق | **BR-045** · BR-051 | UC-020 | `payroll_lines` · `journal_entries` | `Payroll.Approve` | `PayrollApproved` | AC-121 | TEST-114 | ح-45 | 🟢 |
| **FR-WRK-012** منع الخصم المزدوج للسلفة | **BR-049** | **UC-018** · UC-020 | `worker_advances` · `payroll_lines` · 🟢 `treasury_movements` | `WorkerAdvances.Manage` | `WorkerAdvanceRecorded` | ⭐ **AC-123** | TEST-116 · 117 | ح-49 | 🟢 |
| **FR-WRK-014** `تم القبض` محسوب | **BR-050** | UC-019 | `worker_payments` · `payroll_lines` | `WorkerPayments.Manage` | `WorkerPaymentRecorded` | AC-125 | TEST-119 | ح-50 | 🟢 |
| **FR-WRK-017** منع ازدواج المقاول | **BR-052** | UC-020 | `payroll_lines` · `subcontractor_settlements` | `Payroll.Approve` | `PayrollApproved` | AC-128 | TEST-122 | ح-52 | 🔵 |
| **FR-PAY-002** منع التوزيع بلا سياسة | **BR-225** | UC-026 | `engineer_salary_allocations` | `Payroll.Approve` | `SalaryAllocationApplied` | AC-131 | TEST-125 | ملحق التقرير §7.1 | 🟢 |
| **FR-PAY-004** الافتراضي بلا توزيع | **BR-226** | UC-026 | `engineer_salary_allocation_rules` | — | — | AC-133 | TEST-127 | ملحق التقرير §7.3 | 🟢 |
| **FR-SUP-007** تمييز نسبة الأعمال | **BR-011** | UC-021 | `subcontractor_settlements` · `payment_order_party_shares` | `Subcontractors.Settle` | `SubcontractorSettlementApproved` | AC-142 | TEST-136 | ملحق الأجور §8.2 | 🟢 |
| **FR-SUP-015** منع القيد الثاني | **BR-051** · BR-169 | UC-021 | `recoverable_expense_claims` · `journal_entries` | `Subcontractors.Settle` | `RecoverableClaimCreated` | AC-150 | TEST-144 | النموذج المفاهيمي §5.9 | 🟢 |
| **FR-SUP-016** مسار استرداد واحد | **BR-105** | UC-021 | `recoverable_expense_allocations` | `Subcontractors.Settle` | `RecoverableAllocated` | AC-151 | TEST-145 | النموذج المفاهيمي §5.9 | 🟢 |
| **FR-CLM-005** سطور استقطاع مفتوحة | **BR-144** … **BR-146** | **UC-023** | `claim_deduction_lines` | `Claims.Manage` | `ClaimApproved` | AC-160 | TEST-156 | ح-144…146 | 🟢 |
| **FR-CLM-006** سلّم المعالجة الثلاثي | **BR-173** | **UC-023** | `deduction_types` · `deduction_rule_versions` · `claim_deduction_lines` | `Deductions.Manage` | `DeductionRuleVersionCreated` | AC-161 | TEST-157 | ح-173 | 🟢 |
| **FR-CLM-008** تغيير القاعدة لا يمسّ المعتمد | **BR-146** | UC-023 | `claim_deduction_lines` | — | — | AC-163 | TEST-159 | ح-146 | 🟢 |
| **FR-CLM-011** تطبيق نسبة الشركة الخارجية | ⭐ **BR-170** · BR-008 | **UC-025** | `_agreement_versions` · `payment_order_party_shares` | `PaymentOrders.Manage` | `ExternalCompanyShareApplied` | ⭐ **AC-007** · AC-154 · AC-155 | TEST-148 … 151 | Patch 1.1.1 §1 | 🟢 |
| **FR-CLM-012** تفعيل الاتفاق | **BR-170** | UC-025 | `_agreement_versions` · `agreement_documents` | `Agreements.Activate` | `ExternalCompanyAgreementActivated` | AC-166 | TEST-162 | ح-170 | 🟢 |
| **FR-CLM-013** إصدار اتفاق جديد | **BR-007** | UC-025 | `_agreement_versions` | `Agreements.Manage` | `AgreementVersionCreated` | ⭐ **AC-007** | TEST-163 | ح-7 | 🟢 |
| **FR-CLM-026** استقلال الاعتراف عن التحصيل | **BR-014** · BR-013 | UC-024 | `payment_terms` · `journal_entries` | `Claims.Approve` | `ClaimApproved` | AC-179 | TEST-176 | ح-14 | 🟢 |
| **FR-CLM-027** التصحيح بمستند | **BR-178** | UC-023 | `claims` · `claim_progress` | `Claims.Manage` | `ClaimAdjusted` | **AC-005** | TEST-177 | ح-178 | 🟢 |
| **FR-CLM-029** شرط الرد حدث لا تاريخ | **BR-125** · **BR-166** | UC-024 | `release_condition_versions` · `retention_register` | `Retentions.Release` | `RetentionReleased` | AC-181 | TEST-179 | العقد البند 8 | 🟢 |
| **FR-CLM-033** تاريخ الاستعلام إلزامي | **BR-150** · BR-172 | UC-032 | `retention_register` | `Reports.View` | — | AC-185 | TEST-183 | ح-150 | 🟢 |
| **FR-CLM-036** فصل الضمانين | **BR-091** | UC-024 | `guarantees` · `retention_register` | `Claims.Manage` | `GuaranteeIssued` | AC-188 | TEST-186 | ح-91 · العقد البند 12 | 🟢 |
| **FR-CLM-042** التقريب مرة واحدة | **BR-093** · **BR-094** | UC-023 | كل الجداول المالية | — | — | AC-194 | TEST-192 | ح-93 · ح-94 | 🔵 |
| **FR-REP-005** إصدار التقرير الشهري | **BR-215** … **BR-242** | **UC-026** | `monthly_report_versions` · `_snapshots` · `stored_documents` | `Reports.Monthly.Generate` | `MonthlyReportGenerated` | **AC-030** … **AC-039** | TEST-195 … 203 | ملحق التقرير | 🟢 |
| **FR-REP-007** فصل التكلفة عن النقد | **BR-222** · BR-223 | UC-026 | `monthly_report_versions` | — | — | ⭐ **AC-037** | TEST-209 | ملحق التقرير §6 | 🟢 |
| **FR-REP-008** استبعاد غير المعتمد | **BR-219** | UC-026 | `monthly_report_snapshots` | — | — | ⭐ **AC-038** | TEST-210 | ح-219 | 🟢 |
| **FR-REP-010** إصدار جديد لا تعديل | **BR-229** · **BR-230** | **UC-027** | `monthly_report_versions` | `Reports.Monthly.Generate` | `MonthlyReportVersionCreated` | ⭐ **AC-044** · **AC-045** | TEST-212 · 213 | ح-229 | 🟢 |
| **FR-REP-011** إعادة الإرسال بلا إصدار | **BR-230** | **UC-028** | `monthly_report_delivery_attempts` | `Reports.Monthly.ManageRecipients` | `MonthlyReportResent` | ⭐ **AC-042** · **AC-046** | TEST-214 | ح-230 | 🟢 |
| **FR-REP-016** رسالة منفصلة لكل مستلم | **BR-236** | UC-026 | `monthly_report_delivery_attempts` | — | `MonthlyReportSent` | **AC-040** | TEST-219 | ح-236 | 🟢 |
| **FR-REP-018** سجل إرسال لكل مستلم | **BR-238** · **BR-244** | UC-028 | `monthly_report_delivery_attempts` | — | `MonthlyReportDeliveryFailed` | AC-204 | TEST-221 | ح-238 | 🟢 |
| **FR-REP-019** فشل مستلم لا يوقف الباقين | **BR-239** | UC-026 | `monthly_report_versions` | — | `MonthlyReportDeliveryFailed` | ⭐ **AC-041** | TEST-222 | ح-239 | 🟢 |
| **FR-REP-021** حجب التقرير بلا `Payroll.View` | **BR-235** · BR-245 | UC-028 | `monthly_report_recipients` | `Payroll.View` | `AccessDenied` | ⭐ **AC-047** | TEST-224 | ح-235 | 🟢 |
| **FR-REP-022** Idempotency للمهمة | **BR-241** | UC-026 | `monthly_report_versions` | — | `BackgroundJobResult` | ⭐ **AC-043** | TEST-225 | ح-241 | 🟢 |
| **FR-REP-023** عطل البريد لا يفقد التقرير | **BR-242** | UC-026 | `monthly_report_versions` · `stored_documents` | — | `MonthlyReportDeliveryFailed` | ⭐ **AC-049** | TEST-226 | ح-242 | 🟢 |
| **FR-REP-027** محتويات Snapshot التسعة | **BR-228** | UC-026 | `monthly_report_snapshots` + سطوره الأربعة | — | `MonthlyReportVersionCreated` | ⭐ **AC-044** | TEST-230 | ملحق التقرير §4 | 🟢 |
| **FR-IMP-001** دفعة ببصمة | **BR-151** · **BR-155** | **UC-029** | `import_batches` | `Imports.Run` | `ImportBatchStarted` | AC-208 | TEST-232 | ح-151 · ح-155 | 🟢 |
| **FR-IMP-005** لا مضاعفة | **BR-103** · **BR-188** | UC-029 | `import_rows` · `record_lineage` | `Imports.Run` | `ImportBatchCompleted` | ⭐ **AC-212** | TEST-236 | ح-103 | 🟢 |
| **FR-IMP-006** رفض صفوف المجاميع | 📊 ب-13 | UC-029 | `import_rows` | `Imports.Run` | `ImportRowRejected` | AC-213 | TEST-237 | `excel-source-mapping` §2 | 🟢 |
| **FR-IMP-007** مطابقة المجموع | 📊 ب-13 | UC-029 | `import_batches` · `reconciliation_checks` | `Imports.Run` | `ImportBatchAborted` | ⭐ **AC-214** | TEST-238 | `excel-source-mapping` §2 | 🟢 |
| **FR-IMP-008** إعادة حساب العمود المحسوب | 📊 ب-14 | UC-029 | `import_rows` | `Imports.Run` | `ImportRowRejected` | AC-215 | TEST-239 | `excel-source-mapping` §3 | 🟢 |
| **FR-IMP-011 · 012** سياسة `LEGACY` | **BR-181** | UC-029 | `claims` · `claim_series` | `Imports.Run` | `ImportBatchCompleted` | AC-218 · AC-219 | TEST-242 · 243 | ح-181 | 🟢 |
| **FR-AUD-001** ذرّية حدث التدقيق | ⭐ **BR-186** | كل UC مالي | `audit_events` | — | **هو الحدث نفسه** | ⭐ **AC-001** · **AC-002** · **AC-014** | TEST-249 · 250 | ملحق التدقيق §7 | 🟢 |
| **FR-AUD-002** Append-only | **BR-187** | **UC-031** | `audit_events` | `Audit.View` | — | ⭐ **AC-010** | TEST-251 | ملحق التدقيق §6 | 🟢 |
| **FR-AUD-006** لا رصيد من التدقيق | **BR-184** · **BR-246** | UC-032 | — | — | — | AC-226 | TEST-255 | ح-184 | 🟢 |
| **FR-AUD-008 · 009** الحجب قبل الكتابة | **BR-191** | كل UC | `audit_event_changes` | — | — | ⭐ **AC-015** | TEST-257 · 258 | ملحق التدقيق §9 | 🟢 |
| **FR-AUD-014** Serilog لا يُفشل المعاملة | **BR-194** | كل UC مالي | — | — | — | ⭐ **AC-013** | TEST-263 | ملحق التدقيق §12.5 | 🟢 |
| **FR-AUD-016** لا رواتب في Serilog | **BR-245** · BR-191 | UC-020 · UC-026 | — | — | — | AC-230 | TEST-265 | ح-245 | 🟢 |

### 1.99 🟢 ⭐ متطلبات الباتش v1.2.1 — BR-247 … BR-262

| المتطلب | قاعدة العمل | حالة الاستخدام | الكيانات | الصلاحية | حدث التدقيق | معيار القبول | حالة الاختبار | المصدر | الحالة |
|---|---|---|---|---|---|---|---|---|---|
| **FR-OPS-030** أمر إسناد واحد لكل عملية | **BR-247** · BR-248 | UC-004 | `operations` · `assignment_orders` | `Operations.Manage` | `AssignmentOrderLinked` | **AC-233** | TEST-233 | باتش v1.2.1 §2 | 🟢 |
| **FR-OPS-031** أمر الإسناد Read-only للمهندس | **BR-249** | UC-011 | `expenses` · `assignment_orders` | `Expenses.Create` | — | **AC-234** | TEST-234 | باتش §2.2 | 🟢 |
| **FR-ACC-001** حدث مالي لكل أثر قابل للترحيل | **BR-250** | UC-008 · UC-014 | `financial_events` | `System` | `FinancialEventCreated` | **AC-235** | TEST-235 | باتش §3 | 🟢 |
| **FR-ACC-002** قيد متوازن | **BR-251** | UC-014 | `journal_entries` · `journal_entry_lines` | `Accounting.Post` | `JournalEntryPosted` | **AC-236** | TEST-236 | باتش §3.4 | 🟢 |
| **FR-ACC-003** لا تعديل لقيد مرحَّل | **BR-252** | UC-014 | `journal_entries` | — | `JournalEntryReversed` | **AC-237** | TEST-237 | باتش §3.4 | 🟢 |
| **FR-ACC-004** الفترة المقفلة تمنع الترحيل | **BR-253** | UC-030 | `fiscal_periods` | `Accounting.ClosePeriod` | `FinancialPeriodClosed` · `FinancialPeriodReopened` | **AC-238** | TEST-238 | باتش §3.4 | 🟢 |
| **FR-ACC-005** الأرصدة مشتقة | **BR-254** | UC-031 | `journal_entry_lines` · `ledger_period_balances` | `Reports.View` | — | **AC-239** | TEST-239 | باتش §5 | 🟢 |
| **FR-TRS-001** حركة خزينة بسند | **BR-250** · BR-254 | UC-008 | `treasury_movements` · `financial_events` | `Treasury.Manage` | `TreasuryMovementRecorded` | **AC-240** | TEST-240 | باتش §4 | 🟢 |
| **FR-TRS-002** مطابقة الخزينة بالأستاذ | **BR-255** | UC-031 | `treasury_movements` · `journal_entry_lines` | `Accounting.Reconcile` | `ReconciliationExceptionRaised` | **AC-241** | TEST-241 | باتش §4.3 | 🟢 |
| ⭐ **FR-TRS-003** لا خصم نقدي مزدوج | ⭐ **BR-256** | **UC-014** | `expenses` · `custody_ledger_entries` · `treasury_movements` | `Expenses.Approve` | `ExpenseApproved` | ⭐ **AC-242** | TEST-242 · 243 | باتش §7 | 🟢 |
| ⭐ **FR-TRS-004** صرف العهدة أربعة آثار في معاملة | ⭐ **BR-257** | **UC-008** | `financial_events` · `treasury_movements` · `custody_ledger_entries` · `journal_entries` · `audit_events` | `Custody.Disburse` | `CustodyDisbursed` | ⭐ **AC-244** | TEST-244 · 245 | باتش §6 | 🟢 |
| **FR-EXP-030** أربع حالات للمصروف | **BR-258** | UC-011 … UC-014 | `expenses` | — | `ExpenseSubmitted` | **AC-246** | TEST-246 | باتش §9 | 🟢 |
| **FR-EXP-031** فصل حالة المزامنة | **BR-259** | UC-013 | `sync_submissions` · `sync_exceptions` | `System` | `SyncBlocked` · `SyncExceptionResolved` | **AC-247** | TEST-247 | باتش §9.3 | 🟢 |
| **FR-RPT-030** محاولات إرسال متعددة | **BR-260** | UC-029 | `monthly_report_deliveries` · `monthly_report_delivery_attempts` | `Reports.Send` | `MonthlyReportResent` | **AC-248** | TEST-248 | باتش §10 | 🟢 |
| **FR-DB-001** دلالة غياب معلَنة | **BR-261** · **BR-262** | — | كل الجداول ذات التفرّد المشروط | — | — | **AC-249** | TEST-249 | باتش §11 | 🟢 |

> 🟢 ⭐ **خمس عشرة صفًا جديدًا — وكلها مربوطة بقاعدة ومعيار قبول وحالة اختبار.** ⛔ **ولا متطلب يتيم.**

### 1.100 🟢 ⭐ متطلبات الباتش v2.0.1 — BR-263 … BR-272

| المتطلب | قاعدة العمل | حالة الاستخدام | الكيانات | الصلاحية | حدث التدقيق | معيار القبول | حالة الاختبار | المصدر | الحالة |
|---|---|---|---|---|---|---|---|---|---|
| ⭐ **FR-ACC-010** اكتمال آثار الحدث قبل الترحيل | ⭐ **BR-264** | UC-008 · UC-014 | `financial_events` · `journal_entries` · `treasury_movements` · `custody_ledger_entries` | `Accounting.Post` | `FinancialEventCreated` · `JournalEntryPosted` | ⭐ **AC-250** | TEST-250 … 253 | باتش v2.0.1 §2 | 🟢 |
| **FR-ACC-011** قيد واحد لكل حدث | **BR-265** | UC-014 | `journal_entries` | `Accounting.Post` | `JournalEntryPosted` | **AC-251** | TEST-254 | باتش §3 | 🟢 |
| **FR-ACC-012** مستند تصحيح لكل عكس | **BR-266** · **BR-267** | UC-032 | `accounting_adjustments` · `financial_events` | `Accounting.Adjust` | `JournalEntryReversed` | **AC-252** | TEST-255 · 256 | باتش §4 | 🟢 |
| **FR-ACC-013** قواعد سطور القيد | **BR-272** | UC-014 | `journal_entry_lines` | `Accounting.Post` | — | **AC-253** | TEST-257 … 259 | باتش §5 | 🟢 |
| **FR-TRS-010** تخصيص الحركة بحدث مالي | **BR-268** | UC-025 | `treasury_movement_allocations` · `financial_events` | `Treasury.Manage` | `TreasuryMovementAllocated` | **AC-254** | TEST-260 | باتش §6 | 🟢 |
| **FR-TRS-011** طريقة التسوية نظامية | **BR-263** | UC-011 · UC-014 | `financial_events` | — | — | **AC-255** | TEST-261 | باتش §7 | 🟢 |
| **FR-TRS-012** قواعد أداة الدفع | **BR-269** | UC-008 · UC-025 | `treasury_movements` · `cheques` · `treasury_accounts` | `Treasury.Manage` | `TreasuryMovementRecorded` | **AC-256** | TEST-262 | باتش §8 | 🟢 |
| **FR-IMP-010** فصل الأرصدة الافتتاحية | **BR-270** | UC-027 | `financial_events` · `accounting_adjustments` · `import_batches` | `Import.Run` | `OpeningBalanceApproved` | **AC-257** | TEST-263 | باتش §9 | 🟢 |
| **FR-IMP-011** قيود مجمَّعة بأبعادها | **BR-271** | UC-027 | `journal_entries` · `record_lineage` · `import_rows` | `Import.Run` | `ImportBatchCompleted` | **AC-258** | TEST-264 · 265 | باتش §9 | 🟢 |

---

## 2. ⭐ التتبع العكسي — كل قاعدة عمل ← ما يطبّقها

### 2.1 القواعد المالية الأساسية

| BR | المتطلبات التي تطبّقها | UC | الحالة |
|---|---|---|---|
| **BR-001 · BR-002** | FR-009 · FR-001 | UC-003 | ✅ |
| **BR-003 … BR-012** | FR-CLM-011 … FR-CLM-014 | UC-025 | ✅ |
| **BR-013 · BR-014** | FR-CLM-025 · FR-CLM-026 | UC-024 | ✅ |
| **BR-015 … BR-023** | FR-019 … FR-024 · FR-EXP-015 · FR-EXP-016 | UC-006 · UC-009 | ✅ |
| **BR-024 … BR-029** | FR-CUS-001 … FR-CUS-010 | UC-008 · UC-016 | ✅ |
| **BR-030 … BR-033** | FR-010 · FR-011 | UC-003 | ✅ |
| **BR-034 … BR-045** | FR-WRK-002 … FR-WRK-010 | UC-017 · UC-020 | ✅ |
| **BR-046 … BR-052** | FR-WRK-001 · FR-WRK-009 … FR-WRK-017 | UC-017 … UC-020 | ✅ |
| **BR-053 … BR-059** | FR-001 … FR-008 · FR-IMP-009 | UC-003 · UC-029 | ✅ |
| **BR-060 … BR-073** | FR-CLM-001 … FR-CLM-010 · FR-CLM-039 … FR-CLM-041 | UC-022 · UC-023 | ✅ |
| **BR-074 … BR-077** | FR-025 … FR-027 | UC-004 · UC-022 | ✅ |
| **BR-078 … BR-088** | FR-CLM-015 … FR-CLM-025 | UC-024 | ✅ |
| **BR-089 … BR-091** | FR-CLM-035 … FR-CLM-038 | UC-024 | ✅ |
| **BR-093 · BR-094** | FR-CLM-042 · FR-CLM-043 | UC-023 | 🔵 |
| **BR-096 · BR-097** | FR-IMP-013 · FR-CLM-044 | UC-029 | ✅ |
| **BR-098 · BR-099** | FR-CUS-001 · Domain 18 | UC-008 | ✅ |
| **BR-100 … BR-106** | ⭐ **كل طبقات منع الازدواج** — FR-004 · FR-005 · FR-EXP-019 · FR-IMP-005 · FR-SUP-010 · FR-SUP-016 · FR-WRK-018 | كل UC مالي | ✅ |
| **BR-107 … BR-112** | FR-028 … FR-033 | UC-004 | 🔵 |
| **BR-114 … BR-121 · BR-124 · BR-125 · BR-127 · BR-128** | FR-CLM-011 · FR-CLM-029 · FR-CLM-035 · FR-CLM-040 · FR-SUP-008 · FR-SUP-011 · FR-030 | UC-024 · UC-025 | ✅ |
| **BR-131 … BR-139** | FR-019 · FR-023 · FR-EXP-015 … FR-EXP-017 | UC-006 · UC-009 | ✅ |
| **BR-140 … BR-143** | FR-CUS-001 … FR-CUS-006 | UC-008 · UC-015 | ✅ |
| **BR-144 … BR-147** | FR-CLM-005 … FR-CLM-009 | UC-023 | ✅ |
| **BR-148 · BR-149** | FR-012 · FR-IMP-015 | UC-029 · UC-030 | ✅ |
| **BR-150** | FR-CLM-033 · FR-REP-004 | UC-032 | ✅ |
| **BR-151 … BR-155** | FR-IMP-001 … FR-IMP-004 | UC-029 | ✅ |
| **BR-156 … BR-160** | ADR-001 · ADR-002 · ADR-016 · FR-009 | — | ✅ |
| **BR-161 … BR-169** | ⭐ **القيود المصحَّحة** — مطبَّقة في النموذج المنطقي §17.2 وملحق ب | — | ✅ |
| **BR-170** | ⭐ FR-CLM-011 · FR-CLM-012 | UC-025 | ✅ |
| **BR-173** | ⭐ FR-CLM-006 · FR-CLM-028 | UC-023 | ✅ |
| **BR-174 … BR-177** | ⭐ **حُسمت في النموذج المنطقي** §17.2 · FR-006 · FR-WRK-007 | — | ✅ |
| **BR-178 · BR-179** | FR-CLM-027 · Domain 18 | UC-023 | ✅ |
| **BR-180** | ⭐ **قواعد النزاهة §5** | — | ✅ |
| **BR-181** | FR-IMP-011 · FR-IMP-012 | UC-029 | ✅ |
| **BR-182 … BR-195** | FR-AUD-001 … FR-AUD-018 | UC-031 | ✅ |
| **BR-196 … BR-214** | FR-015 … FR-024 · FR-EXP-001 … FR-EXP-022 | UC-004 … UC-015 | ✅ |
| **BR-215 … BR-246** | FR-REP-005 … FR-REP-028 · FR-PAY-002 … FR-PAY-005 | UC-026 … UC-028 | ✅ |

### 2.2 ⭐ القواعد بلا متطلب مباشر — مُعلَنة ومبرَّرة

> ⭐ **ليست كل قاعدة قاعدةَ سلوك برمجي — وهذا تصنيف صحيح لا نقص في التتبع.**

| BR | لماذا لا متطلب مباشر | أين تظهر بدلًا من ذلك |
|---|---|---|
| **BR-092** | ⭐ **حقيقة رصد** — شيك الضمان 400,563 غائب عن ورقة `شيكات` | **أثرها في FR-CLM-035** · و**ملاحظات الترحيل في Domain 9** |
| **BR-095** | ⭐ **إحصاء يبرر BR-093** — 26% من القيم بكسور > منزلتين | **مبرِّر NUM-01 … NUM-08** |
| **BR-113** | قيمة عقد التيسير 8,011,262.40 | **بيانات عقد تُدخَل — لا سلوك** |
| **BR-116** | مدة التنفيذ 24 شهرًا | **بيانات عقد** · ويُحسب النهو بـFR-029 |
| **BR-122** | القيمة التعاقدية تشمل الأجور والإقامة والتأمين | **بند وصفي — لا سلوك** |
| **BR-123** | المزروعات في عهدة المقاول | **بند وصفي** |
| **BR-126** | حق إلغاء العقد عند إيقاف الأعمال | **بند وصفي** |
| **BR-129** | سريان اللائحة التنفيذية للجهة المالكة | **بند وصفي** |
| **BR-130** | ⭐ **البند 14 مفقود · ولا بند اختصاص ولا تحكيم** | ⭐ **RISK-012 — يحتاج مراجعة قانونية لا كودًا** |
| **BR-171 · BR-172** | ⭐ **قواعد تسمية ووصف للأرقام المنشورة** | ⭐ **مطبَّقة في §2.4 و§25.2 وفي كل تقرير — ولا تُنفَّذ كمتطلب وظيفي** |

---

## 3. ⭐ تتبع حالات الاستخدام

| UC | المتطلبات | BR الرئيسية | Entities | AC |
|---|---|---|---|---|
| **UC-001** تسجيل الدخول | FR-AUTH-001 · FR-AUTH-002 | BR-193 | `users` · `roles` | AC-050 · AC-051 |
| **UC-002** المستخدمون والصلاحيات | FR-AUTH-003 … FR-AUTH-007 | **BR-235** · BR-195 | `users` · `user_permissions` | AC-052 … AC-055 · **AC-047** |
| **UC-003** الشركات والأطراف | FR-001 … FR-012 | BR-001 · BR-002 · **BR-100** · BR-160 | `parties` + 8 كيانات | AC-060 … AC-071 |
| **UC-004** العملية وأمر الإسناد | FR-015 · FR-016 · FR-025 … FR-031 · FR-034 | ⭐ **BR-197** | `operations` · `assignment_orders` · `boq_items` | **AC-022** · AC-074 |
| **UC-005** المواقع | FR-017 · FR-018 | **BR-198** | `sites` | AC-077 · AC-078 |
| **UC-006** تكليف مهندس | FR-019 · FR-022 | ⭐ **BR-017** · BR-202 | `engineer_site_assignments` | AC-075 · AC-076 · AC-081 |
| **UC-007** إنهاء تكليف | FR-020 · FR-021 | **BR-020** · BR-202 | `engineer_site_assignments` | AC-079 · **AC-080** |
| **UC-008** تسجيل عهدة | FR-CUS-001 · FR-CUS-002 | **BR-025** · BR-140 | `custody_ledger_entries` | AC-092 … AC-094 |
| **UC-009** مصروف كمسودة | FR-EXP-001 … FR-EXP-005 · FR-023 · FR-024 | ⭐ **BR-203** … **BR-208** | `expenses` | **AC-016** … **AC-021** |
| **UC-010** مزامنة Offline | FR-EXP-016 · FR-IMP-005 | ⭐ **BR-212** · BR-103 | `expenses` · `audit_events` | **AC-008** · **AC-028** |
| **UC-011** إرسال للمراجعة | FR-EXP-006 · FR-EXP-015 | **BR-210** · BR-211 | `expenses` | **AC-022** · **AC-023** |
| **UC-012** طلب توضيح | FR-EXP-008 · FR-EXP-022 | BR-209 · BR-207 | `expense_reviews` | **AC-004** |
| **UC-013** تعديل وإعادة إرسال | FR-EXP-009 | BR-209 | `expenses` | **AC-003** |
| **UC-014** اعتماد المصروف | FR-EXP-007 · FR-EXP-019 | ⭐ **BR-213** · **BR-186** | `expenses` · `custody_ledger_entries` · `journal_entries` | **AC-001** · **AC-002** · **AC-027** |
| **UC-015** تصحيح معتمد | FR-EXP-011 · FR-CUS-006 | **BR-213** · BR-178 | `expense_corrections` | **AC-005** · **AC-029** |
| **UC-016** رصيد العهدة | FR-CUS-007 · FR-CUS-008 | **BR-028** · BR-027 | `custody_ledger_entries` | AC-098 · **AC-099** |
| **UC-017** يومية عامل | FR-WRK-001 … FR-WRK-005 | **BR-046** · BR-176 | `attendance_entries` | AC-111 · AC-112 |
| **UC-018** سلفة عامل | FR-WRK-011 … FR-WRK-013 | **BR-048** · BR-049 | `worker_advances` | AC-122 · **AC-123** |
| **UC-019** دفعة عامل | FR-WRK-014 · FR-WRK-015 | **BR-050** | `worker_payments` | AC-125 · AC-126 |
| **UC-020** كشف الأجور | FR-WRK-007 … FR-WRK-010 · FR-WRK-016 · FR-WRK-017 | **BR-045** · BR-047 · BR-052 | `payroll_periods` · `payroll_lines` | AC-118 … AC-128 |
| **UC-021** المورد وحسابه | FR-SUP-001 … FR-SUP-018 | BR-165 · **BR-169** | `supplier_*` · `recoverable_*` | AC-136 … AC-153 |
| **UC-022** إنشاء مستخلص | FR-CLM-001 … FR-CLM-004 · FR-CLM-010 | **BR-060** · BR-163 · BR-181 | `claims` · `claim_progress` | AC-156 … AC-159 |
| **UC-023** الاستقطاعات | FR-CLM-005 … FR-CLM-009 · FR-CLM-028 | ⭐ **BR-173** · BR-144 … BR-147 | `claim_deduction_lines` | AC-160 … AC-165 |
| **UC-024** أمر الدفع | FR-CLM-015 … FR-CLM-025 · FR-CLM-029 … FR-CLM-038 | **BR-078** · BR-079 · BR-087 | `payment_orders` + 5 كيانات | AC-168 … AC-190 |
| **UC-025** نسبة الشركة الخارجية | FR-CLM-011 … FR-CLM-014 | ⭐ **BR-170** · BR-008 | `_agreement_versions` · `_party_shares` | ⭐ **AC-007** · AC-166 |
| **UC-026** التقرير الشهري | FR-REP-005 … FR-REP-009 · FR-REP-016 … FR-REP-027 | ⭐ **BR-215** … **BR-246** | `monthly_report_*` (10 كيانات) | **AC-030** … **AC-049** |
| **UC-027** إعادة الإصدار | FR-REP-010 | **BR-229** · BR-230 | `monthly_report_versions` | **AC-044** · **AC-045** |
| **UC-028** إعادة الإرسال | FR-REP-011 · FR-REP-020 · FR-REP-021 | **BR-230** · BR-236 · BR-239 | `_delivery_attempts` | **AC-041** · **AC-042** · **AC-046** · **AC-047** |
| **UC-029** استيراد Excel | FR-IMP-001 … FR-IMP-014 | ⭐ **BR-151** … **BR-155** · BR-181 | `import_batches` · `import_rows` | **AC-009** · AC-208 … AC-221 |
| **UC-030** أخطاء الاستيراد | FR-IMP-015 · FR-IMP-017 | BR-153 · BR-188 | `import_resolutions` | AC-222 · **AC-009** |
| **UC-031** سجل التدقيق | FR-AUD-002 · FR-AUD-011 · FR-AUD-012 | **BR-187** · BR-192 | `audit_events` | **AC-010** · **AC-011** |
| **UC-032** كشف حساب أو تقرير | FR-REP-001 … FR-REP-004 · FR-REP-028 · FR-AUD-006 | **BR-184** · BR-150 · BR-246 | قراءة من الدفاتر | AC-185 · AC-207 · AC-226 |

---

## 4. ⭐ تتبع المتطلبات غير الوظيفية

| NFR | المصدر | المعيار | TEST | الحالة |
|---|---|---|---|---|
| **NFR-SEC-001** … **NFR-SEC-004** | BR-018 · BR-191 · BR-235 · BR-232 | صفر انتهاك | TEST-008 · TEST-257 · TEST-006 · TEST-217 | 🟢 |
| **NFR-SEC-005** … **NFR-SEC-008** | 🔵 مقترح | أرقام مقترحة بسببها | — | 🔵 |
| **NFR-SEC-009** · **NFR-SEC-010** | BR-187 · BR-192 | منع تقني + صفر وصول | TEST-251 · TEST-010 | 🟢 |
| **NFR-SEC-011** … **NFR-SEC-014** | BR-245 · BR-236 · BR-189 · BR-191 | — | TEST-224 · TEST-219 | 🟢 |
| **NFR-PERF-001** … **NFR-PERF-008** | 🔵 **كلها مقترحة بأحجام 2026 المرصودة** | أرقام محددة | اختبارات أداء | 🔵 |
| **NFR-REL-001** … **NFR-REL-008** | BR-186 · BR-103 · BR-028 · BR-187 · BR-093 · BR-194 · 📊 · BR-231 | صفر انتهاك | TEST-249 … TEST-263 | 🟢 |
| **NFR-REL-009** · **NFR-REL-010** | 🔵 مقترح | 99.5% · 48 ساعة | — | 🔵 |
| **NFR-OFF-001** … **NFR-OFF-006** | ADR-008 · BR-212 · BR-137 | — | TEST-097 · TEST-236 | 🟢 |
| **NFR-OFF-007** · **NFR-OFF-008** | 🔵 مقترح | — | — | 🔵 |
| **NFR-OPS-001** … **NFR-OPS-004** | 🔵 مقترح | RPO 15د · RTO 4س | — | 🔵 |
| **NFR-OPS-005** … **NFR-OPS-012** | BR-233 · BR-190 · BR-193 · BR-194 · BR-157 | — | — | 🟢 |
| **NFR-OPS-013** | 🔵 مقترح | ضعف أحجام 2026 | — | 🔵 |
| **NFR-USE-001** · **NFR-USE-003** · **NFR-USE-005** · **NFR-USE-006** · **NFR-USE-009** | BR-203 · BR-205 · BR-027 · BR-055 · BR-207 | — | TEST-060 … TEST-077 | 🟢 |
| **NFR-USE-002** · **NFR-USE-004** · **NFR-USE-007** · **NFR-USE-008** | 🔵 مقترح | — | — | 🔵 |

---

## 5. ⭐ تتبع المخاطر إلى الضوابط

| RISK | الضابط | FR / BR | TEST |
|---|---|---|---|
| **RISK-001** Serilog بديلًا للتدقيق | BR-183 · BR-192 | FR-AUD-014 · FR-AUD-010 | TEST-263 |
| **RISK-002** رصيد من التدقيق | BR-184 · BR-246 | FR-AUD-006 | TEST-255 |
| **RISK-003** التدقيق خارج المعاملة | BR-186 | FR-AUD-001 | TEST-249 · TEST-250 |
| **RISK-004** تسريب في لقطات القيم | BR-191 | FR-AUD-008 | TEST-257 |
| **RISK-005** حذف سجلات تدقيق | BR-190 | FR-AUD-013 | TEST-262 |
| **RISK-009** التفاف بحدث زائف | BR-187 | FR-AUD-007 | TEST-256 |
| **RISK-011** ⭐ **الترحيل يضاعف الأثر** | BR-100 … BR-106 | FR-IMP-005 … FR-IMP-008 | TEST-236 … TEST-239 |
| **RISK-012** عيوب العقد التوثيقية | BR-130 | ⭐ **مراجعة قانونية لا كود** | — |
| **RISK-014** رصيد المحتجزات الافتتاحي | BR-171 | FR-CLM-033 | TEST-183 |
| **RISK-015** 11,138,575 معلَّقة | DEP-07 | FR-CLM-034 | TEST-184 |
| **RISK-016** دمج طرفين حقيقيين | BR-100 · BR-168 | FR-004 · FR-005 | TEST-015 · TEST-016 |
| **RISK-017** إزاحة ثابتة للكتل | 📊 | FR-IMP-008 | TEST-239 |
| **RISK-018** الملف يتغيّر يوميًا | BR-151 · BR-155 | FR-IMP-001 | TEST-232 |
| **RISK-019** تسريب الرواتب | BR-235 · BR-245 | FR-REP-021 · FR-AUD-016 | TEST-224 · TEST-265 |
| **RISK-020** توزيع راتب عشوائي | BR-225 · BR-226 | FR-PAY-002 · FR-PAY-004 | TEST-125 · TEST-127 |
| **RISK-021** إضافة حقل «طريقة الدفع» | BR-208 | FR-EXP-001 | TEST-060 |
| **RISK-022** جعل المرفق إجباريًا | BR-207 | FR-EXP-005 | TEST-076 · TEST-077 |
| **RISK-023** `CHECK` عابر للجداول | BR-180 | ⭐ **قواعد النزاهة §5** | — |
| **RISK-024** الاعتماد على الواجهة | BR-018 · BR-138 | FR-AUTH-008 · FR-EXP-015 | TEST-008 · TEST-087 … 096 |
| **RISK-025** 21.8 مليون غير مصنَّف | BR-149 | FR-012 · FR-IMP-015 | TEST-023 · TEST-246 |

---

## 6. ⭐ تتبع القرارات المفتوحة إلى ما تعلّقه

| OQ | يعلّق | هل يمنع التصميم؟ |
|---|---|---|
| **OQ-001** مدة الاحتفاظ بالتدقيق | FR-AUD-013 · NFR-OPS-006 | ⛔ **لا — الحقل موجود والسياسة تُملأ** |
| **OQ-003** سقف الاعتماد | FR-AUTH-011 · FR-EXP-007 | ⛔ **لا — الفارغ يعني بلا سقف** |
| **OQ-004** التقارير الأساسية | FR-REP-001 … FR-REP-004 | ⛔ **لا — يطيل النسخة الأولى فقط** |
| **OQ-016** ⭐ المعالجة المحاسبية للاسترداد | FR-SUP-015 · قيد الإنشاء | ⛔ **لا — قيد محاسبي واحد والدورة تدعم الاثنين** |
| **OQ-019** ⭐ تصنيف المحتجزات | FR-CLM-028 · **15,819,871.65** | ⛔ **لا — قيمة حقل مرجعي** |
| **OQ-020 · OQ-021 · OQ-022** | تصنيف **20,573,254** | ⛔ **لا** |
| **OQ-023** التسويات المتطابقة | FR-SUP-010 · الترحيل | ⛔ **لا — تُستورد بعلامة** |
| **OQ-024** أمر الإسناد 64 | الترحيل — **7,802,857** | ⛔ **لا — التصميم يمنع التكرار مستقبلًا** |
| **OQ-025** الأرصدة الافتتاحية | FR-IMP-016 | ⛔ **لا — الحقل موجود** |
| **OQ-027** المستفيد البنكي | FR-CLM-024 | ⛔ **لا — تنبيه تدقيق** |
| **OQ-029** صف `U251` | RC-20 | ⛔ **لا — فرق معلوم موقَّع** |
| **OQ-030** بصمات ناقصة | FR-IMP-001 | ⛔ **لا للتصميم — ✅ نعم للترحيل** |
| 🟡 **قاعدة توزيع الراتب** | FR-PAY-003 | ⛔ **لا — الكيان مُصدَّر ويقبل أي خيار** |

> ## ✅ **صفر قرار مفتوح يمنع التصميم — وصفر Blocking Decision.**

---

## 7. ⭐ الإحصاء النهائي

| البند | العدد |
|---|---|
| **قواعد العمل** | **246** |
| منها **لها متطلب مباشر** | **235** |
| منها **بلا متطلب — مُعلَنة ومبرَّرة** | **11** |
| **المتطلبات الوظيفية** | **226** |
| منها ⭐ **حرجة** | **48** |
| **المتطلبات غير الوظيفية** | **54** |
| **حالات الاستخدام** | **32** |
| **معايير القبول** | **232** |
| منها ⭐ **بصيغة Given/When/Then** | **24** |
| **حالات الاختبار** | **267** |
| **الكيانات المنطقية** | **125** |
| **أحداث التدقيق** | **96** |
| **الصلاحيات الدقيقة** | **49** |
| **القرارات المعمارية** | **18** |
| **القرارات المفتوحة** | **30** |
| **المخاطر** | **25** |
| ⭐ **المتطلبات اليتيمة** | ⭐ **صفر** |
| ⭐ **معايير القبول بلا اختبار** | ⭐ **صفر** |
| ⭐ **Blocking Decisions** | ⭐ **صفر** |
