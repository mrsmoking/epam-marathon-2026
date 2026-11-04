@api @L1
Feature: Group pre-order participants

  The customer who booked the table (the owner) invites friends to a group
  pre-order. Every guest fills a cart of their own, and before anything goes to
  the restaurant the owner looks at who has joined, what each of them chose and
  what it all comes to.

  Background:
    Given the restaurant's menu has the dishes:
      | dish       | price |
      | Borscht    | 12.50 |
      | Khachapuri | 9.00  |
    And a customer has booked a table for 4 guests

  Rule: The owner sees every participant with their dishes and totals

    @smoke
    Scenario: The owner sees what each guest has chosen
      Given "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has added 1 "Khachapuri" to the cart
      And "Anna" has submitted the cart
      And "Ben" has joined the group
      And "Ben" has added 1 "Khachapuri" to the cart
      When the owner looks at the participants of the group
      Then the owner sees the participants:
        | participant | dishes                      | total | cart          |
        | the owner   | nothing                     | 0     | not submitted |
        | Anna        | 2 x Borscht, 1 x Khachapuri | 34.00 | submitted     |
        | Ben         | 1 x Khachapuri              | 9.00  | not submitted |
      And the group total is 43.00

    Scenario: A guest who has not chosen anything yet is listed too
      Given "Anna" has joined the group
      When the owner looks at the participants of the group
      Then the owner sees the participants:
        | participant | dishes  | total | cart          |
        | the owner   | nothing | 0     | not submitted |
        | Anna        | nothing | 0     | not submitted |

    # Decided for this implementation (see README): a group nobody has joined still lists the owner.
    Scenario: A group nobody has joined yet lists only the owner
      When the owner looks at the participants of the group
      Then the owner sees the participants:
        | participant | dishes  | total | cart          |
        | the owner   | nothing | 0     | not submitted |
      And the group total is 0

    Scenario: The group total follows a guest changing the cart
      Given "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      When "Anna" adds 1 "Khachapuri" to the cart
      And the owner looks at the participants of the group
      Then the group total is 34.00
      When "Anna" takes 2 "Borscht" out of the cart
      And the owner looks at the participants of the group
      Then the group total is 9.00

  Rule: Only the owner can see the participants

    @negative
    Scenario Outline: Nobody but the owner can see the participants
      Given "Anna" has joined the group
      When <someone> looks at the participants of the group
      Then they are told <answer>

      Examples: People who are signed in
        | someone          | answer                            |
        | another customer | they may not see the participants |
        | the waiter       | they may not see the participants |

      Examples: People who are not signed in
        | someone                        | answer           |
        | a guest of the group           | to sign in first |
        | a visitor who is not signed in | to sign in first |

  Rule: The group has to exist

    @negative
    Scenario: The owner asks for a group that does not exist
      When the owner looks at the participants of a group that does not exist
      Then the owner is told the group could not be found
