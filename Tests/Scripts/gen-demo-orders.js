// Generates Settings/System/Defaults/demo-orders.json — the 100 preset orders a demo store is
// seeded with. Deterministic (fixed LCG seed) so re-running it produces byte-identical output and a
// regenerated file diffs cleanly.
//
// Names are romanised on purpose: this file is shipped configuration and the repo's CJK sweep covers
// *.json, so a Chinese customer name here would read as a localisation violation every time it runs.

const fs = require('node:fs');

// Deterministic pseudo-random: a plain 32-bit LCG. Math.random() would make every regeneration a
// whole-file diff.
let seed = 20260802;
function next() {
  seed = (seed * 1664525 + 1013904223) >>> 0;
  return seed / 4294967296;
}
const pick = (arr) => arr[Math.floor(next() * arr.length)];
const between = (lo, hi) => lo + Math.floor(next() * (hi - lo + 1));
const money = (lo, hi, step) => Math.round((lo + Math.floor(next() * ((hi - lo) / step + 1)) * step) * 100) / 100;

const FIRST = ['Amelia','Benjamin','Chloe','Daniel','Elena','Felix','Grace','Henry','Isabel','Jonas',
  'Karin','Liam','Mila','Noah','Olivia','Priya','Quentin','Rosa','Samuel','Tara','Umar','Vera',
  'Wesley','Xenia','Yusuf','Zara','Adrian','Bianca','Caleb','Dora','Emil','Farah','Gustav','Hana',
  'Ivan','Julia','Kenji','Lucia','Marco','Nadia','Oscar','Petra','Rafael','Sofia','Tomas','Ulrike',
  'Viktor','Wendy','Yara','Zeno'];

const LAST = ['Clarke','Novak','Whitfield','Okonkwo','Ferreira','Lindqvist','Tanaka','Duarte',
  'Hoffmann','Baptiste','Moreau','Kowalski','Andersen','Rossi','Vargas','Lam','Chen','Wong',
  'Nakamura','Petrov','Silva','Hughes','Marchetti','Sorensen','Delgado'];

const STREETS = ['Queen Street West','Bloor Street East','King Street West','Dundas Street West',
  'College Street','Yonge Street','Spadina Avenue','Bathurst Street','Danforth Avenue',
  'Eglinton Avenue West','Front Street East','Adelaide Street West'];

const CITIES = ['Toronto','North York','Scarborough','Etobicoke','Markham','Mississauga'];

const AREA = ['416','647','905','437','289'];

const NOTES = [
  'Customer prefers a slightly looser fit through the waist.',
  'Second fitting booked for the following week.',
  'Fabric supplied by the customer.',
  'Rush job — agreed on a shorter turnaround.',
  'Matching trousers to follow on a separate order.',
  'Sleeve buttons to be kept as they are.',
  'Lining replaced in the same colour.',
  'Customer asked to keep the original hem allowance.',
  'Collect on a Saturday if possible.',
  'Paid the deposit in store, balance on collection.',
  null, null, null, null,
];

const REFUND_REASONS = ['CustomerDoesNotWant','ServiceUnsatisfactory','ProductIssue','PriceTooHigh','Other'];

const PRODUCTS = ['Jackets','TiesBowtie','Qipao','LeatherShoes','Other'];

const GARMENTS = {
  jacket: ['length','chest','waist','shoulder','sleeve','sitAround'],
  vest: ['length','chest','waist','shoulder','sitAround'],
  shirt: ['length','chest','shoulder','sleeve','neck','cuff','sitAround'],
  pants: ['waist','hip','inseam','outseam','thigh','knee','bottom','rise'],
  blouse: ['length','bust','waist','shoulder','sleeve','neck'],
  dress: ['length','bust','underBust','waist','hip','shoulder','sleeve'],
  qipao: ['length','bust','underBust','waist','hip','shoulder','neck','sleeve'],
};

// Plausible centimetre ranges per term, so the printed measurement sheet reads like a real one.
const TERM_RANGE = {
  length: [66, 112], chest: [88, 116], waist: [70, 108], hip: [88, 118], shoulder: [40, 50],
  sleeve: [56, 66], sitAround: [92, 118], neck: [35, 45], cuff: [22, 28], bust: [82, 108],
  underBust: [70, 94], inseam: [72, 86], outseam: [96, 112], thigh: [52, 66], knee: [38, 48],
  bottom: [32, 42], rise: [24, 32],
};

const DEPOSIT_METHODS = ['Cash','DebitCard','CreditCard','Etransfer'];

// Day offsets. Deliberately front-loaded: a settlement report defaults to month-to-date, so a set
// spread evenly backwards leaves the default view empty when the demo store is created on the 1st.
// Twenty of the hundred land inside the last week whatever day that is.
function buildOffsets() {
  const offsets = [];
  for (let i = 0; i < 20; i++) offsets.push(i % 7);           // this week, guaranteed
  for (let i = 0; i < 24; i++) offsets.push(7 + between(0, 23));   // the last month
  for (let i = 0; i < 28; i++) offsets.push(31 + between(0, 59));  // the two before it
  for (let i = 0; i < 28; i++) offsets.push(91 + between(0, 89));  // the quarter before that
  return offsets;
}

// Status by age: recent work is still in the shop, older work has been collected.
function statusFor(daysAgo, roll) {
  if (roll < 0.06) return daysAgo > 20 ? 'Cancelled' : 'Processing';
  if (roll < 0.11) return daysAgo > 20 ? 'Returned' : 'Processing';
  if (daysAgo <= 10) return roll < 0.75 ? 'Processing' : 'Shipped';
  if (daysAgo <= 30) return roll < 0.35 ? 'Processing' : 'Completed';
  return roll < 0.08 ? 'Shipped' : 'Completed';
}

function section(subtotal, status, depositShare) {
  const deposit = Math.round(subtotal * depositShare * 100) / 100;
  const depositMethod = pick(DEPOSIT_METHODS);
  const finished = status === 'Completed' || status === 'Shipped';
  return {
    subtotal,
    deposit,
    depositMethod,
    finalMethod: next() < 0.7 ? depositMethod : pick(DEPOSIT_METHODS),
    depositReceived: true,
    cleared: finished,
  };
}

function measurements(garmentId) {
  const values = {};
  for (const term of GARMENTS[garmentId]) {
    const [lo, hi] = TERM_RANGE[term];
    values[term] = String(between(lo, hi));
  }
  return values;
}

const offsets = buildOffsets();
const orders = [];

for (let i = 0; i < 100; i++) {
  const first = pick(FIRST);
  const last = pick(LAST);
  const daysAgo = offsets[i];
  const status = statusFor(daysAgo, next());
  const refunded = status === 'Cancelled' || status === 'Returned';

  // Which services this order carries. Weighted towards the two everyday ones.
  const roll = next();
  let shape;
  if (roll < 0.34) shape = 'alteration';
  else if (roll < 0.55) shape = 'readyMade';
  else if (roll < 0.75) shape = 'customMade';
  else if (roll < 0.85) shape = 'alteration+readyMade';
  else if (roll < 0.95) shape = 'alteration+customMade';
  else shape = 'all';

  const order = {
    customerName: `${first} ${last}`,
    phoneNumber: `+1 ${pick(AREA)}-555-${String(100 + i).padStart(4, '0')}`,
    email: `${first.toLowerCase()}.${last.toLowerCase()}@example.com`,
    address: `${between(2, 980)} ${pick(STREETS)}, ${pick(CITIES)}`,
    status,
    orderDaysAgo: daysAgo,
    pickupDaysAfterOrder: between(7, 32),
    notes: pick(NOTES),
  };

  if (refunded) {
    order.statusReasonCategory = pick(REFUND_REASONS);
    if (order.statusReasonCategory === 'Other')
      order.statusReason = 'Customer moved away before collection.';
  }

  if (shape.includes('alteration'))
    order.alteration = section(money(45, 320, 5), status, pick([0.3, 0.4, 0.5]));

  if (shape.includes('readyMade') || shape === 'all') {
    const items = [];
    const count = between(1, 3);
    for (let n = 0; n < count; n++)
      items.push({ productId: pick(PRODUCTS), quantity: between(1, 3), unitPrice: money(35, 480, 5) });

    const subtotal = Math.round(items.reduce((sum, it) => sum + it.quantity * it.unitPrice, 0) * 100) / 100;
    order.clothing = section(subtotal, status, pick([0.3, 0.5]));
    order.items = items;
  }

  if (shape.includes('customMade') || shape === 'all') {
    const garment = pick(Object.keys(GARMENTS));
    order.customMade = {
      ...section(money(480, 2200, 20), status, pick([0.3, 0.4, 0.5])),
      garmentId: garment,
      measurements: measurements(garment),
    };
    // The custom-made section is priced from the record, not from a stored subtotal — the model
    // computes CustomMadeSubtotal off each record's Price. Rename it so nothing reads it as one.
    order.customMade.price = order.customMade.subtotal;
    delete order.customMade.subtotal;
  }

  // Which service the order is FILED under, for the list's primary column.
  if (order.customMade) order.serviceType = 'CustomMade';
  else if (order.clothing) order.serviceType = 'ReadyMade';
  else order.serviceType = 'Alterations';

  orders.push(order);
}

// Oldest first, so the shipped file reads as a history.
orders.sort((a, b) => b.orderDaysAgo - a.orderDaysAgo);

const payload = { version: 1, orders };
fs.writeFileSync(process.argv[2], JSON.stringify(payload, null, 2) + '\n', 'utf8');

const counts = {};
for (const o of orders) counts[o.status] = (counts[o.status] || 0) + 1;
console.log('orders:', orders.length, counts);
console.log('within 7 days:', orders.filter(o => o.orderDaysAgo < 7).length);
console.log('sections:', {
  alteration: orders.filter(o => o.alteration).length,
  clothing: orders.filter(o => o.clothing).length,
  customMade: orders.filter(o => o.customMade).length,
});

