// Placeholder data shaped after `parties`/`party_roles` — replace with a real
// repository call once the Parties read API exists. Do not treat as real records.
//
// Role codes come only from the documented vocabulary (logical-data-model-ar.md
// §2 / decisions/0001): Engineer, Worker, Supplier, Contractor, Debtor,
// Creditor, Partner, Company.

import 'party_summary.dart';

const List<PartySummary> mockParties = [
  PartySummary(
    id: 'mock-01',
    displayName: 'م. أحمد عبد الله (مثال)',
    roleCodes: ['Engineer'],
    status: PartyStatus.active,
  ),
  PartySummary(
    id: 'mock-02',
    displayName: 'مورد أسمدة تجريبي',
    roleCodes: ['Supplier', 'Creditor'],
    status: PartyStatus.active,
  ),
  PartySummary(
    id: 'mock-03',
    displayName: 'شركة مقاولات تجريبية',
    roleCodes: ['Contractor', 'Partner', 'Creditor'],
    status: PartyStatus.active,
  ),
  PartySummary(
    id: 'mock-04',
    displayName: 'عامل يومية (مثال)',
    roleCodes: ['Worker'],
    status: PartyStatus.active,
  ),
  PartySummary(
    id: 'mock-05',
    displayName: 'طرف مستورد بدون تصنيف',
    roleCodes: [],
    status: PartyStatus.needsClassification,
  ),
  PartySummary(
    id: 'mock-06',
    displayName: 'عميل مدين تجريبي',
    roleCodes: ['Debtor'],
    status: PartyStatus.active,
  ),
  PartySummary(
    id: 'mock-07',
    displayName: 'الشركة الشقيقة (مثال)',
    roleCodes: ['Company', 'Partner'],
    status: PartyStatus.active,
  ),
  PartySummary(
    id: 'mock-08',
    displayName: 'مورد معدات سابق',
    roleCodes: ['Supplier'],
    status: PartyStatus.inactive,
  ),
  PartySummary(
    id: 'mock-09',
    displayName: 'م. سارة محمود (مثال)',
    roleCodes: ['Engineer', 'Debtor'],
    status: PartyStatus.active,
  ),
  PartySummary(
    id: 'mock-10',
    displayName: 'مقاول باطن تجريبي',
    roleCodes: ['Contractor'],
    status: PartyStatus.needsClassification,
  ),
];
