# Plan_Should_MarkRequirementSteps_When_DeferredGroupReadsRequirementOfSchemaTwo

## requirement inside the fragment

```yaml
operation:
  - document: |
      {
        user(id: 1) {
          soft
          id @fusion__requirement
          ... @defer {
            id
            id @fusion__requirement
            sku
            sku @fusion__requirement
            total
            email @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 1
    expandedNodes: 2
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        user(id: 1) {
          ... {
            email
          }
          id
        }
      }
  - id: 2
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_2($__fusion_1_id: Int!) {
        userByIdB(id: $__fusion_1_id) {
          soft
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_1_id
        selectionMap: >-
          id
    dependencies:
      - id: 1
  - id: 3
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_3($__fusion_2_id: Int!) {
        userByIdB(id: $__fusion_2_id) {
          id
          sku
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_2_id
        selectionMap: >-
          id
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
  - id: 4
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_4($__fusion_3_email: String!, $__fusion_4_sku: String) {
        userByEmail(email: $__fusion_3_email) {
          total(sku: $__fusion_4_sku)
        }
      }
    source: $.userByEmail
    target: $.user
    requirements:
      - name: __fusion_3_email
        selectionMap: >-
          email
      - name: __fusion_4_sku
        selectionMap: >-
          sku
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 3
deliveryGroups:
  - id: 0
    path: $.user

```

## alias inside the fragment

```yaml
operation:
  - document: |
      {
        user(id: 1) {
          id
          id @fusion__requirement
          sku
          ... @defer {
            s: sku
            total
            id @fusion__requirement
            email @fusion__requirement
            sku @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 1
    expandedNodes: 2
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        user(id: 1) {
          id
          ... {
            email
          }
        }
      }
  - id: 2
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_2($__fusion_1_id: Int!) {
        userByIdB(id: $__fusion_1_id) {
          sku
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_1_id
        selectionMap: >-
          id
    dependencies:
      - id: 1
  - id: 3
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_3($__fusion_2_id: Int!) {
        userByIdB(id: $__fusion_2_id) {
          s: sku
          sku
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_2_id
        selectionMap: >-
          id
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
  - id: 4
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_4($__fusion_3_email: String!, $__fusion_4_sku: String) {
        userByEmail(email: $__fusion_3_email) {
          total(sku: $__fusion_4_sku)
        }
      }
    source: $.userByEmail
    target: $.user
    requirements:
      - name: __fusion_3_email
        selectionMap: >-
          email
      - name: __fusion_4_sku
        selectionMap: >-
          sku
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 3
deliveryGroups:
  - id: 0
    path: $.user

```

## alias outside the fragment

```yaml
operation:
  - document: |
      {
        user(id: 1) {
          id
          id @fusion__requirement
          k: sku
          ... @defer {
            sku
            sku @fusion__requirement
            total
            id @fusion__requirement
            email @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 1
    expandedNodes: 2
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        user(id: 1) {
          id
          ... {
            email
          }
        }
      }
  - id: 2
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_2($__fusion_1_id: Int!) {
        userByIdB(id: $__fusion_1_id) {
          k: sku
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_1_id
        selectionMap: >-
          id
    dependencies:
      - id: 1
  - id: 3
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_3($__fusion_2_id: Int!) {
        userByIdB(id: $__fusion_2_id) {
          sku
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_2_id
        selectionMap: >-
          id
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
  - id: 4
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_4($__fusion_3_email: String!, $__fusion_4_sku: String) {
        userByEmail(email: $__fusion_3_email) {
          total(sku: $__fusion_4_sku)
        }
      }
    source: $.userByEmail
    target: $.user
    requirements:
      - name: __fusion_3_email
        selectionMap: >-
          email
      - name: __fusion_4_sku
        selectionMap: >-
          sku
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 3
deliveryGroups:
  - id: 0
    path: $.user

```

## condition on the plain copy

```yaml
operation:
  - document: |
      query($c: Boolean!) {
        user(id: 1) {
          id
          id @fusion__requirement
          soft
          sku @include(if: $c)
          ... @defer {
            sku
            sku @fusion__requirement
            total
            id @fusion__requirement
            email @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 1
    expandedNodes: 2
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        user(id: 1) {
          id
          ... {
            email
          }
        }
      }
  - id: 2
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_2($c: Boolean!, $__fusion_1_id: Int!) {
        userByIdB(id: $__fusion_1_id) {
          soft
          sku @include(if: $c)
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_1_id
        selectionMap: >-
          id
    forwardedVariables:
      - c
    dependencies:
      - id: 1
  - id: 3
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_3($__fusion_2_id: Int!) {
        userByIdB(id: $__fusion_2_id) {
          sku
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_2_id
        selectionMap: >-
          id
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
  - id: 4
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_4($__fusion_3_email: String!, $__fusion_4_sku: String) {
        userByEmail(email: $__fusion_3_email) {
          total(sku: $__fusion_4_sku)
        }
      }
    source: $.userByEmail
    target: $.user
    requirements:
      - name: __fusion_3_email
        selectionMap: >-
          email
      - name: __fusion_4_sku
        selectionMap: >-
          sku
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 3
deliveryGroups:
  - id: 0
    path: $.user

```

## switch

```yaml
operation:
  - document: |
      query($v: Boolean!) {
        user(id: 1) {
          soft
          id @fusion__requirement
          email @fusion__requirement
          sku @fusion__requirement
          total @skip(if: $v)
          ... @include(if: $v) {
            ... @defer(if: $v) {
              total
              email @fusion__requirement
              sku @fusion__requirement
              id @fusion__requirement
            }
          }
        }
      }
    hash: 123456789101112
    searchSpace: 4
    expandedNodes: 13
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        user(id: 1) {
          id
          email
        }
      }
  - id: 2
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_2($__fusion_1_id: Int!) {
        userByIdB(id: $__fusion_1_id) {
          soft
          sku
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_1_id
        selectionMap: >-
          id
    dependencies:
      - id: 1
  - id: 3
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_3($__fusion_2_email: String!, $__fusion_3_sku: String) {
        userByEmail(email: $__fusion_2_email) {
          total(sku: $__fusion_3_sku)
        }
      }
    source: $.userByEmail
    target: $.user
    requirements:
      - name: __fusion_2_email
        selectionMap: >-
          email
      - name: __fusion_3_sku
        selectionMap: >-
          sku
    conditions:
      - variable: $v
        passingValue: false
    dependencies:
      - id: 1
      - id: 2
  - id: 5
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_5($__fusion_5_sku: String, $__fusion_6_email: String!) {
        userByEmail(email: $__fusion_6_email) {
          total(sku: $__fusion_5_sku)
        }
      }
    source: $.userByEmail
    target: $.user
    requirements:
      - name: __fusion_5_sku
        selectionMap: >-
          sku
      - name: __fusion_6_email
        selectionMap: >-
          email
    conditions:
      - variable: $v
        passingValue: true
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 6
  - id: 6
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_6($__fusion_7_id: Int!) {
        userByIdB(id: $__fusion_7_id) {
          sku
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_7_id
        selectionMap: >-
          id
    conditions:
      - variable: $v
        passingValue: true
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.user
    ifVariable: $v

```

## only the requiring field

```yaml
operation:
  - document: |
      {
        user(id: 1) {
          soft
          id @fusion__requirement
          ... @defer {
            total
            email @fusion__requirement
            sku @fusion__requirement
            id @fusion__requirement
          }
        }
      }
    hash: 123456789101112
    searchSpace: 2
    expandedNodes: 7
nodes:
  - id: 1
    type: Operation
    schema: a
    operation: |
      query Op_123456789101112_1 {
        user(id: 1) {
          ... {
            email
          }
          id
        }
      }
  - id: 2
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_2($__fusion_1_id: Int!) {
        userByIdB(id: $__fusion_1_id) {
          soft
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_1_id
        selectionMap: >-
          id
    dependencies:
      - id: 1
  - id: 4
    type: Operation
    schema: c
    operation: |
      query Op_123456789101112_4($__fusion_3_sku: String, $__fusion_4_email: String!) {
        userByEmail(email: $__fusion_4_email) {
          total(sku: $__fusion_3_sku)
        }
      }
    source: $.userByEmail
    target: $.user
    requirements:
      - name: __fusion_3_sku
        selectionMap: >-
          sku
      - name: __fusion_4_email
        selectionMap: >-
          email
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
      - id: 5
  - id: 5
    type: Operation
    schema: b
    operation: |
      query Op_123456789101112_5($__fusion_5_id: Int!) {
        userByIdB(id: $__fusion_5_id) {
          sku
        }
      }
    source: $.userByIdB
    target: $.user
    requirements:
      - name: __fusion_5_id
        selectionMap: >-
          id
    deliveryGroups:
      - 0
    dependencies:
      - id: 1
deliveryGroups:
  - id: 0
    path: $.user

```
